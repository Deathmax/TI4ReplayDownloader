TI4ReplayDownloader
===================

Downloads, archives and uploads replays for The International 4.

Requirements
------------
* Running on Mono on Linux
* Plowshare
* [matchurls](https://github.com/RJacksonm1/matchurls) running locally

Command Line Syntax
-------------------
    TI4ReplayDownloader <series name> [action]

Series name defines the name of the set of replays.

Action can be one of the following:

* list - List down all matches in the league listing for TI4 into matches.txt
* [empty] - Reads from a text file (series name.txt) with match ids produced from list, downloads the demos, decompresses them, archives them into a zip, and uploads them.
