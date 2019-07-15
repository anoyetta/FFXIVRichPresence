using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DiscordRPC;
using Newtonsoft.Json;

namespace FFXIVRichPresenceRunner
{
    internal class Program
    {
        private const string ClientID = "478143453536976896";

        private const int SW_HIDE = 0;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static Process _ffxivProcess;

        private static string _lastFc = String.Empty;

        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs eventArgs)
            {
                File.WriteAllText("RichPresenceException.txt", eventArgs.ExceptionObject.ToString());

                Process.GetCurrentProcess().Kill();
            };

            var pid = int.Parse(args[0]);

            _ffxivProcess = null;
            _ffxivProcess = pid == -1 ? Process.GetProcessesByName("ffxiv_dx11")[0] : Process.GetProcessById(pid);

#if !DEBUG
            ShowWindow(GetConsoleWindow(), SW_HIDE);
#endif

            Run();

            while (true)
            {
                // Don't wanna burn CPUs
                Thread.Sleep(1);
            }
        }

        private static bool DoesFfxivProcessExist()
        {
            _ffxivProcess.Refresh();
            return !_ffxivProcess.HasExited;
        }

        private static readonly RichPresence DefaultPresence = new RichPresence
        {
            Details = "Unknown",
            State = "",
            Assets = new Assets
            {
                LargeImageKey = "zone_default",
                LargeImageText = "",
                SmallImageKey = "class_0",
                SmallImageText = ""
            }
        };

        public static async void Run()
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            var discordManager = new Discord(DefaultPresence, ClientID);

            var game = new Nhaama.FFXIV.Game(_ffxivProcess);
            var ignores = LoadIgnoreList();

            Console.WriteLine(game.Process.GetSerializer().SerializeObject(game.Definitions, Formatting.Indented));

            discordManager.SetPresence(DefaultPresence);

            while (true)
            {
                if (!DoesFfxivProcessExist())
                {
                    discordManager.Deinitialize();
                    Environment.Exit(0);
                }

                game.Update();

                if (game.ActorTable == null)
                {
                    discordManager.SetPresence(DefaultPresence);
                    Thread.Sleep(5000);
                    continue;
                }

                if (game.ActorTable.Length > 0)
                {
                    var player = game.ActorTable[0];

                    if (player.ActorID == 0)
                    {
                        discordManager.SetPresence(DefaultPresence);
                        Thread.Sleep(5000);
                        continue;
                    }

                    var territoryType = game.TerritoryType;

                    var placename = await XivApi.GetPlaceNameZoneForTerritoryType(territoryType);

                    if (placename == "default" || placename == "Norvrandt")
                    {
                        placename = await XivApi.GetPlaceNameForTerritoryType(territoryType);
                    }

                    var zoneAsset = "zone_" + Regex.Replace(placename.ToLower(), "[^A-Za-z0-9]", "");

                    var fcName = player.CompanyTag;

                    if (fcName != string.Empty)
                    {
                        _lastFc = fcName;
                        fcName = $" <{fcName}>";
                    }
                    else if (_lastFc != string.Empty)
                    {
                        fcName = $" <{_lastFc}>";
                    }

                    var worldName = await XivApi.GetNameForWorld(player.World);

                    if (player.World != player.HomeWorld)
                        worldName = $"{worldName} (🏠{await XivApi.GetNameForWorld(player.HomeWorld)})";

                    discordManager.SetPresence(new RichPresence
                    {
                        Details = isIgnore(player.Name) ?
                            "** SECRET **" :
                            $"{player.Name}{fcName}",
                        State = worldName,
                        Assets = new Assets
                        {
                            LargeImageKey = zoneAsset,
                            LargeImageText = await XivApi.GetPlaceNameForTerritoryType(territoryType),
                            SmallImageKey = $"class_{player.Job}",
                            SmallImageText = await XivApi.GetJobName(player.Job) + " Lv." + player.Level
                        }
                    });
                }

                Thread.Sleep(1000);
            }

            bool isIgnore(string name)
            {
                if (ignores == null ||
                    ignores.Length < 1)
                {
                    return false;
                }

                return ignores.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static string[] LoadIgnoreList()
        {
            var file = Assembly.GetExecutingAssembly().Location
                .Replace(".exe", ".ignore.txt");

            if (!File.Exists(file))
            {
                return default;
            }

            var ignores = new List<string>();
            using (var reader = new StreamReader(file, new UTF8Encoding(false)))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine().Trim();

                    if (!string.IsNullOrEmpty(line) &&
                        !line.StartsWith("#"))
                    {
                        ignores.Add(line);
                    }
                }
            }

            return ignores.ToArray();
        }
    }
}
