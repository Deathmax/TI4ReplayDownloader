using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TI4ReplayDownloader
{
    public class ConsoleExt
    {
        private static readonly List<string> _messages = new List<string>(51);
        private static readonly List<ProgressBar> _progressBars = new List<ProgressBar>();
        private static int _barRows;
        private static int _currentMessageLoc;
        private static bool _started = false;
        private static int _nukeBars = 0;
        private static object LogMutex = new object();

        public static void TestRender()
        {
            var i = 0;
            var bar = new ProgressBar();
            var bar2 = new ProgressBar() { Message = "2 SUp sup sup"};
            var bar3 = new ProgressBar() { Message = "3 SUp sup sup" };
            var bar4 = new ProgressBar() { Message = "4 SUp sup sup" };
            _progressBars.Add(bar);
            _progressBars.Add(bar2);
            _progressBars.Add(bar3);
            _progressBars.Add(bar4);
            while (true)
            {
                if (i <= 100)
                {
                    bar.Progress = i;
                    bar.Message = "Test Message Left".PadRight(Console.WindowWidth);
                    var num = string.Format("{0} %", i);
                    bar.Message = bar.Message.Insert(bar.Message.Length - num.Length, num);
                }
                if (i > 100)
                {
                    bar.Message = "Done. Destroying Soon.";
                }
                if (i > 105)
                {
                    bar.Destroy = true;
                    bar.Message = "Destroying.";
                    return;
                }
                switch (i)
                {
                    case 10:
                        bar2.Destroy = true;
                        break;
                    case 40:
                        bar4.Destroy = true;
                        bar3.Destroy = true;
                        break;
                }
                for (int h = 0; h < new Random().Next(1, 5); h++)
                    if (!(i < 55 && i > 40))
                        Log("Testing {0}:{1}", i, h);
                i++;
                Thread.Sleep(500);
            }
        }

        public static void Log(int index, string format, params object[] param)
        {
            Log(index + " - " + String.Format(format, param));
        }

        public static void Log(string format, params object[] param)
        {
            Log(String.Format(format, param));
        }

        public static void Log(int index, string message)
        {
            AddMessage(string.Format("{3} - {0} - {1}", DateTime.Now, message, index));
        }

        public static void Log(string message)
        {
            AddMessage(string.Format("{0} - {1}", DateTime.Now, message));
        }

        public static void Start()
        {
            Console.Clear();
            new Thread(RenderLoop).Start();
        }

        public static void AddProgressBar(ProgressBar bar)
        {
            _progressBars.Add(bar);
        }

        private static void RenderLoop()
        {
            while (true)
            {
                RenderMessages();
                Thread.Sleep(500);
            }
        }

        private static void AddMessage(string message)
        {
            _messages.Add(message);
            lock (LogMutex)
            {
                File.AppendAllText("log.txt", message + "\n");
            }
        }

        private static void RenderMessages()
        {
            if (Type.GetType("Mono.Runtime") == null)
            {
                Console.BufferWidth = Console.WindowWidth;
                Console.BufferHeight = Console.WindowHeight;
            }

            var width = Console.WindowWidth - 1;
            Console.CursorVisible = false;
            Console.CursorTop = Console.WindowTop + Console.WindowHeight - 1;
            lock (_messages)
            {
                if (_messages.Count > 0)
                {
                    foreach (var message in _messages)
                    {
                        Console.WriteLine(message);
                    }
                    _messages.Clear();
                }
            }
            var orig = Console.CursorTop;
            var pendingRemove = new List<ProgressBar>();
            Console.CursorTop = Console.WindowTop;
            for (int i = 0; i < _progressBars.Count; i++)
            {
                var progressbar = _progressBars[i];
                if (progressbar.Destroy)
                {
                    if (progressbar.DestroyTicks == 4)
                    {
                        pendingRemove.Add(progressbar);
                        Console.Write(new string(' ', width + 1) + ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""));
                        Console.Write(new string(' ', width + 1) + ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""));
                        continue;
                    }
                    progressbar.DestroyTicks++;
                }
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.Cyan;
                var barwidth = (int)(((double)progressbar.Progress/100)*(width - 2));
                //var barwidth = (int)(((width - 1) * progressbar.Progress) / 100d);
                var barstring = new string('\u2592', barwidth) + new string(' ', width - barwidth - 1);
                Console.Write("\r[{0}]" + ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""), barstring);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write((string.Join("", progressbar.Message.Take(width + 1))).PadRight(width + 1) + '\r' +
                              ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""));
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.BackgroundColor = ConsoleColor.Black;
            }
            Console.CursorTop = orig;
            foreach (var pending in pendingRemove)
            {
                _progressBars.Remove(pending);
            }
        }

        /*private static void RenderMessages()
        {
            var width = Console.WindowWidth - 1;
            Console.CursorLeft = 0;
            Console.CursorVisible = false;
            NukeBars();
            if (_messages.Count > 0)
            {
                var writemessage = _messages.Aggregate("",
                                                       (current, message) =>
                                                       current +
                                                       message.PadRight((width + 1) *
                                                                        (int)
                                                                        Math.Ceiling((double)message.Length / width)));
                Console.Write(writemessage + ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""));
                _messages.Clear();
            }
            _barRows = 0;
            var pendingRemove = new List<ProgressBar>();
            var orig = Console.CursorTop;

            for (int i = 0; i < _progressBars.Count; i++)
            {
                var progressbar = _progressBars[i];
                if (progressbar.Destroy)
                {
                    if (progressbar.DestroyTicks == 5)
                    {
                        pendingRemove.Add(progressbar);
                        _barRows++;
                        _barRows++;
                        continue;
                    }
                    progressbar.DestroyTicks++;
                }
                Console.BackgroundColor = ConsoleColor.DarkRed;
                Console.ForegroundColor = ConsoleColor.Cyan;
                var barwidth = (int)(((width - 1) * progressbar.Progress) / 100d);
                var barstring = new string('\u2592', barwidth) + new string(' ', width - barwidth - 1);
                Console.Write("\r[{0}]" + ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""), barstring);
                _barRows++;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write((string.Join("", progressbar.Message.Take(width + 1))).PadRight(width + 1) + '\r' +
                              ((Type.GetType("Mono.Runtime") != null) ? "\n" : ""));
                _barRows++;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.BackgroundColor = ConsoleColor.Black;
            }
            Console.CursorTop = orig;
        }*/

        private static void NukeBars()
        {
            var orig = Console.CursorTop;
            var orighor = Console.CursorLeft;
            for (int i = 0; i < _barRows; i++)
            {
                Console.Write('\r' + new string(' ', Console.WindowWidth));
            }
            _nukeBars = 0;
            Console.CursorTop = orig;
            Console.CursorLeft = orighor;
        }
    }

    public class ProgressBar
    {
        public string Message = "";
        public int Progress = 0;
        public bool Destroy = false;
        public int DestroyTicks = 0;
    }
}
