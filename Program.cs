using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using CG.Web.MegaApiClient;
using ICSharpCode.SharpZipLib.Zip;
using MegaApi;
using Newtonsoft.Json;
using WebClient = System.Net.WebClient;

namespace TI4ReplayDownloader
{
    class Program
    {
        private const string ConfigFile = "config.json";
        private const string MatchesFile = "matches.txt";
        private const string MatchesJsonFile = "matches.json";
        private const string DeadFile = "deadlinks.txt";
        private static readonly Regex URLRegex = new Regex(@"<div class=""url"">(.*)</div>");
        private static string _currentSeries = "";
        private static int _count;
        private static int _inProgressCount;
        private static readonly Queue DownloadQueue = new Queue(); 
        private static readonly List<MatchOutput> Matches = new List<MatchOutput>();
        private static Config _config;

        private static void Main(string[] args)
        {
            ConsoleExt.Start();
            _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigFile));
            if (args.Length == 2)
            {
                _currentSeries = args[0];
                switch (args[1])
                {
                    case "compress":
                        Compress();
                        break;
                    case "list":
                        ListMatches();
                        break;
                    case "check":
                        CheckUrls();
                        break;
                    case "testrender":
                        ConsoleExt.TestRender();
                        break;
                    case "upload":
                        Upload(_currentSeries + ".zip");
                        break;
                }
                return;
            }
            if (args.Length == 1)
            {
                _currentSeries = args[0];
            }
            if (!Directory.Exists(_currentSeries)) Directory.CreateDirectory(_currentSeries);
            ConsoleExt.Log("We reading matches from {0}.", _currentSeries);
            var matchlist = File.ReadAllLines(_currentSeries + ".txt");
            Matches.AddRange(from line in matchlist
                select line.Split(new[] {" - "}, StringSplitOptions.None)
                into parts
                where parts.Length >= 2
                select new MatchOutput
                {
                    MatchId = long.Parse(parts[0]),
                    StartTime = long.Parse(parts[1]),
                    RadiantName = parts[2].Split(new[] {" vs "}, StringSplitOptions.None)[0],
                    DireName = parts[2].Split(new[] {" vs "}, StringSplitOptions.None)[1]
                });
            foreach (string matchid in (from match in matchlist
                where match.Length >= 2
                select match.Split(new[] {" - "}, StringSplitOptions.None)).Select(parts => parts[0]))
            {
                Queue.Synchronized(DownloadQueue).Enqueue(matchid);
            }
            PopQueue();
        }

        private static void PopQueue()
        {
            var ind = 0;
            while (Queue.Synchronized(DownloadQueue).Count != 0)
            {
                if (_count >= 2)
                {
                    Thread.Sleep(500);
                    continue;
                }
                var matchid = (string) Queue.Synchronized(DownloadQueue).Dequeue();
                _count++;
                _inProgressCount++;
                int ind1 = ind;
                new Thread(() => ProcessQueue(matchid, ind1)).Start();
                ind++;
            }
            while (_inProgressCount > 0)
            {
                Thread.Sleep(500);
            }
            Compress();
        }

        private static void ProcessQueue(string matchid, int ind)
        {
            using (var wc = new WebClient {Proxy = null})
            {
                ConsoleExt.Log("We starting with {0}.", matchid);
                var url = wc.DownloadString(_config.MatchURLs.ToString() + matchid);
                if (!url.Contains("http"))
                    ConsoleExt.Log("{0} returned {1}.", matchid, url);
                var demoUrl = URLRegex.Match(url).Groups[1].Value;
                var uri = new Uri(demoUrl);
                var filename = Path.GetFileName(uri.LocalPath);
                ConsoleExt.Log(ind, "We are now downloading {0} from {1}.", filename, demoUrl);
                var progressbar = new ProgressBar {Message = "Downloading " + filename};
                var match = (from wat in Matches where wat.MatchId.ToString(CultureInfo.InvariantCulture) == matchid select wat).First();
                using (var wcThread = new WebClient {Proxy = null})
                {
                    wcThread.DownloadProgressChanged += (sender, eventArgs) =>
                    {
                        progressbar.Progress = eventArgs.ProgressPercentage;
                        var padded = filename.PadRight(Console.WindowWidth);
                        var size = string.Format("{0:#,0}/{1:#,0}KB, {2}%",
                            eventArgs.BytesReceived/1000,
                            eventArgs.TotalBytesToReceive/1000,
                            eventArgs.ProgressPercentage);
                        progressbar.Message =
                            padded.Insert(padded.Length - size.Length,
                                size);
                    };
                    wcThread.DownloadFileCompleted += (sender, eventArgs) =>
                    {
                        _count--;
                        progressbar.Destroy = true;
                        if (eventArgs.Cancelled || eventArgs.Error != null)
                        {
                            progressbar.Destroy = true;
                            progressbar.Message = string.Format(
                                "Downloading of {0} failed. Reason: {1}", filename,
                                eventArgs.Cancelled ? "Cancelled" : eventArgs.Error.ToString());
                            File.AppendAllText(Path.Combine(_currentSeries, "missingdemos.txt"),
                                string.Format("{0} - {3} - {1} vs {2}\n", match.MatchId, match.RadiantName,
                                    match.DireName, UnixTimeStampToDateTime(match.StartTime)));
                            _inProgressCount--;
                            return;
                        }
                        try
                        {
                            ConsoleExt.Log(ind, "We finished downloading {0}, now decompressing.", filename);
                            Process.Start("bunzip2", string.Format("\"{0}/{1}.dem.bz2\"", _currentSeries, matchid))
                                .WaitForExit();
                            ConsoleExt.Log(ind, "Decompressed {0}.", Path.GetFileName(filename));
                            File.AppendAllText("downloadedmatches.txt", matchid + "\n");
                            progressbar.Destroy = true;
                        }
                        catch (Exception ex)
                        {
                            progressbar.Destroy = true;
                            ConsoleExt.Log(ind, "Exception occured finalizing file: {0}", ex.Message);
                        }
                        _inProgressCount--;
                    };
                    ConsoleExt.AddProgressBar(progressbar);
                    wcThread.DownloadFileAsync(uri, Path.Combine(_currentSeries, matchid + ".dem.bz2"));
                }
            }
        }

        private static void Compress()
        {
            if (File.Exists(_currentSeries + ".zip"))
            {
                Upload(_currentSeries + ".zip");
                return;
            }
            ConsoleExt.Log("Compressing {0}", _currentSeries);
            var fastZip = new FastZip();
            const bool recurse = true;
            fastZip.CreateZip(_currentSeries + ".zip", _currentSeries, recurse, null);
            ConsoleExt.Log("We compressed {0}, now we uploading.", _currentSeries);
            Upload(_currentSeries + ".zip");
        }

        static void Upload(string file)
        {
            var plowdone = false;
            var megadone = false;
            new Thread(() =>
            {
                ConsoleExt.Log("Starting up plowup...");
                var startInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    Arguments = "--auth-free=" + _config.MultiUpAuth + " multiup_org \"" + file + "\"",
                    FileName = "plowup"
                };
                var process = new Process {StartInfo = startInfo};
                process.OutputDataReceived += (sender, args) =>
                {
                    ConsoleExt.Log("{0}", args.Data);
                    if (!args.Data.Contains("org/download/")) return;
                    File.AppendAllText("downloadlinks.txt",
                        string.Format("{0} - {1}\r\n", Path.GetFileName(file), args.Data));
                    ConsoleExt.Log("{0} - {1}", Path.GetFileName(file), args.Data);
                    plowdone = true;
                    if (megadone)
                    {
                        Thread.Sleep(500);
                        Environment.Exit(0);
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();
                plowdone = true;
                if (megadone)
                {
                    Thread.Sleep(500); 
                    Environment.Exit(0);
                }
            }).Start();
            Mega.Init(new MegaUser(_config.MegaUser, _config.MegaPass), (a =>
            {
                ConsoleExt.Log("Logged into Mega.");
                ConsoleExt.Log("Uploading {0}.", file);

                var nodes = a.GetNodesSync();
                var nodetouse = (from nod in nodes where nod.Attributes.Name == "TI4 Replays" select nod).First();
                var uploadnode = a.UploadFileSync(nodetouse.Id, file);
                ConsoleExt.Log("Uploaded to mega.");
                uploadnode.Attributes.Name = Path.GetFileName(file);
                a.UpdateNodeAttrSync(uploadnode);

                var client = new MegaApiClient();

                client.Login(_config.MegaUser, _config.MegaPass);
                var nodes2 = client.GetNodes();

                var megaFile = nodes2.FirstOrDefault(node => node.Name != null && node.Name.Contains(Path.GetFileName(file)));
                File.AppendAllText("downloadlinks.txt",
                                   string.Format("{0} - {1}\r\n", Path.GetFileName(file), client.GetDownloadLink(megaFile)));
                ConsoleExt.Log("{0} - {1}", Path.GetFileName(file), client.GetDownloadLink(megaFile));
                megadone = true;
                if (plowdone)
                {
                    Thread.Sleep(500);
                    Environment.Exit(0);
                }
            }), (a =>
            {
                ConsoleExt.Log(
                    "Failed to log into Mega. Error code: {0}",
                    a);
                megadone = true;
                if (plowdone)
                {
                    Thread.Sleep(500);
                    Environment.Exit(0);
                }
            }));
        }

        static void ListMatches()
        {
            var list = new List<MatchOutput>();
            var existinglist = new List<string>();
            if (File.Exists(MatchesJsonFile))
            {
                list = JsonConvert.DeserializeObject<List<MatchOutput>>(File.ReadAllText(MatchesJsonFile));
                existinglist.AddRange(list.Select(match => match.MatchId.ToString()));
            }
            while (true)
            {
                using (var wc = new WebClient {Proxy = null})
                {
                    long lastmatchid = 0;
                    var first = false;
                    ConsoleExt.Log("We are now listing out all matches.");
                    while (true)
                    {
                        ConsoleExt.Log("We are starting with {0}.", lastmatchid);
                        var downloaded =
                            wc.DownloadString(
                                "http://api.steampowered.com/IDOTA2Match_570/GetMatchHistory/v1?key=" + _config.APIKey +
                                "&league_id=600&start_at_match_id=" +
                                lastmatchid);
                        dynamic jsonobject = JsonConvert.DeserializeObject(downloaded);
                        foreach (var leagueMatch in jsonobject.result.matches)
                        {
                            if (first)
                            {
                                first = false;
                                continue;
                            }
                            if (existinglist.Contains(leagueMatch.match_id.ToString()))
                            {
                                ConsoleExt.Log("We have already parsed {0}, we are abandoning", leagueMatch.match_id);
                                lastmatchid = leagueMatch.match_id;
                                continue;
                            }
                            existinglist.Add(leagueMatch.match_id.ToString());
                            ConsoleExt.Log("We are getting {0}.", leagueMatch.match_id);
                            var match = JsonConvert.DeserializeObject(wc.DownloadString(
                                "http://api.steampowered.com/IDOTA2Match_570/GetMatchDetails/v1?key=" + _config.APIKey +
                                "&league_id=600&match_id=" +
                                leagueMatch.match_id));
                            var url = wc.DownloadString(_config.MatchURLs + leagueMatch.match_id);
                            if (!url.Contains("http"))
                                ConsoleExt.Log("{0} returned {1}.", leagueMatch.match_id, url);
                            var demoUrl = URLRegex.Match(url).Groups[1].Value;
                            list.Add(new MatchOutput
                            {
                                RadiantName = match.result.radiant_name,
                                DireName = match.result.dire_name,
                                MatchId = leagueMatch.match_id,
                                StartTime = match.result.start_time,
                                URL = demoUrl,
                                SeriesId = leagueMatch.series_id,
                                SeriesType = leagueMatch.series_type
                            });
                            lastmatchid = leagueMatch.match_id;
                        }
                        first = true;
                        if (jsonobject.result.results_remaining == 0)
                            break;
                    }
                    list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                    File.WriteAllText(MatchesFile,
                        list.Aggregate("",
                            (current, match) =>
                                current +
                                string.Format("{0} - {1} - {2} vs {3} - {4}\n", match.MatchId, match.StartTime,
                                    match.RadiantName,
                                    match.DireName, match.URL)));
                    File.WriteAllText(MatchesJsonFile, JsonConvert.SerializeObject(list));
                    ConsoleExt.Log("Parsed {0} matches.", list.Count);
                }
                Thread.Sleep(10 * 60 * 1000);
            }
        }

        static void CheckUrls()
        {
            var list = new List<MatchOutput>();
            if (File.Exists(MatchesFile))
            {
                var file = File.ReadAllLines(MatchesFile);
                list.AddRange(from line in file
                              select line.Split(new[] { " - " }, StringSplitOptions.None)
                                  into parts
                                  where parts.Length >= 2
                                  select new MatchOutput
                                  {
                                      MatchId = long.Parse(parts[0]),
                                      StartTime = long.Parse(parts[1]),
                                      RadiantName = parts[2].Split(new[] { " vs " }, StringSplitOptions.None)[0],
                                      DireName = parts[2].Split(new[] { " vs " }, StringSplitOptions.None)[1],
                                      URL = parts[3]
                                  });
            }
            foreach (var match in list)
            {
                try
                {
                    var request = (HttpWebRequest) WebRequest.Create(match.URL);
                    request.Method = "HEAD";

                    var response = (HttpWebResponse) request.GetResponse();

                    var success = response.StatusCode == HttpStatusCode.OK;
                    if (!success)
                    {
                        File.AppendAllText(DeadFile, match.URL + "\n");
                        ConsoleExt.Log("{0} is ded.", match.URL);
                    }
                }
                catch (Exception)
                {
                    File.AppendAllText(DeadFile, match.URL + "\n");
                    ConsoleExt.Log("{0} is ded.", match.URL);
                }
            }
            Thread.Sleep(500);
            Environment.Exit(0);
        }

        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            var dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        class MatchOutput
        {
            public string RadiantName;
            public string DireName;
            public long MatchId;
            public long StartTime;
            public string URL;
            public int SeriesId;
            public int SeriesType;
        }
    }
}
