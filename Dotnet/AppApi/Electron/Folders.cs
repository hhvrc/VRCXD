using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VRCX
{
    public partial class AppApiElectron
    {
        public static string _homeDirectory;
        public static string _steamPath;
        public static string _steamUserdataPath;
        public static string _vrcPrefixPath;
        public static string _vrcAppDataPath;
        public static string _vrcCrashesPath;
        public static string _vrcInstallPath;

        static AppApiElectron()
        {
            const string vrchatAppid = "438100";
            _homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _steamUserdataPath = Path.Join(_homeDirectory, ".steam/steam/userdata");
            _steamPath = Path.Join(_homeDirectory, ".local/share/Steam");

            var flatpakSteamPath = Path.Join(_homeDirectory, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
            if (!IsValidSteamPath(_steamPath) && IsValidSteamPath(flatpakSteamPath))
            {
                logger.Info("Flatpak Steam detected.");
                _steamPath = flatpakSteamPath;
            }

            var legacySteamPath = Path.Join(_homeDirectory, ".steam/steam");
            if (!IsValidSteamPath(_steamPath) && IsValidSteamPath(legacySteamPath))
            {
                logger.Info("Legacy Steam path detected.");
                _steamPath = legacySteamPath;
            }

            if (!IsValidSteamPath(_steamPath))
            {
                logger.Error("No valid Steam library found.");
            }

            var libraryFoldersVdfPath = Path.Join(_steamPath, "config/libraryfolders.vdf");
            var vrcLibraryPath = GetLibraryWithAppId(libraryFoldersVdfPath, vrchatAppid);
            if (string.IsNullOrEmpty(vrcLibraryPath))
            {
                logger.Warn("Falling back to default VRChat path as libraryfolders.vdf was not found OR libraryfolders.vdf does not contain VRChat's appid (438100)");
                vrcLibraryPath = _steamPath;
            }
            logger.Info($"Using steam library path {vrcLibraryPath}");
            _vrcPrefixPath = Path.Join(vrcLibraryPath, $"steamapps/compatdata/{vrchatAppid}/pfx");
            _vrcInstallPath = Path.Join(vrcLibraryPath, "steamapps/common/VRChat");
            _vrcAppDataPath = Path.Join(_vrcPrefixPath, "drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat");
            _vrcCrashesPath = Path.Join(_vrcPrefixPath, "drive_c/users/steamuser/AppData/Local/Temp/VRChat/VRChat/Crashes");
        }

        private static bool IsValidSteamPath(string path)
        {
            return File.Exists(Path.Join(path, "config/libraryfolders.vdf"));
        }

        private static string? GetLibraryWithAppId(string libraryFoldersVdfPath, string appId)
        {
            if (!File.Exists(libraryFoldersVdfPath))
                return null;

            string? libraryPath = null;
            foreach (var line in File.ReadLines(libraryFoldersVdfPath))
            {
                // Assumes line will be \t\t"path"\t\t"pathToLibrary"
                if (line.Contains("\"path\""))
                {
                    var parts = line.Split("\t");
                    if (parts.Length < 4)
                        continue;

                    libraryPath = parts[4].Replace("\"", "");
                }

                if (line.Contains($"\"{appId}\"") && Directory.Exists(libraryPath))
                    return libraryPath;
            }

            return null;
        }

        public override string GetVRChatAppDataLocation()
        {
            return _vrcAppDataPath;
        }

        protected override string GetDefaultVRChatPhotosLocation()
        {
            return Path.Join(_vrcPrefixPath, "drive_c/users/steamuser/Pictures/VRChat");
        }

        protected override string GetVRChatCrashDumpsLocation()
        {
            return _vrcCrashesPath;
        }

        public override string GetVRChatScreenshotsLocation()
        {
            return GetLatestSteamScreenshotsLocation(_steamUserdataPath);
        }

        public override void OpenFolderAndSelectItem(string path, bool isFolder = false)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;

            string directoryPath = isFolder ? path : Path.GetDirectoryName(path);
            var commandAttempt = new Dictionary<string, string>
            {
                { "nautilus", $"\"{path}\"" },
                { "nemo", $"\"{path}\"" },
                { "thunar", $"\"{path}\"" },
                { "caja", $"--select \"{path}\"" },
                { "pcmanfm-qt", $"\"{directoryPath}\"" },
                { "pcmanfm", $"\"{directoryPath}\"" },
                { "dolphin", $"--select \"{path}\"" },
                { "konqueror", $"--select \"{path}\"" },
                { "xdg-open", $"\"{directoryPath}\"" }
            };

            foreach (var command in commandAttempt)
            {
                if (!IsCommandAvailable(command.Key))
                    continue;

                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = command.Key,
                            Arguments = command.Value,
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true,
                        }
                    };
                    process.Start();
                    return; // Assume first successful start is enough
                }
                catch
                {
                    // Ignore and try next
                }
            }
        }

        private bool IsCommandAvailable(string command)
        {
            try
            {
                var which = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = command,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                which.Start();
                string result = which.StandardOutput.ReadToEnd();
                which.WaitForExit();
                return which.ExitCode == 0 && !string.IsNullOrWhiteSpace(result);
            }
            catch
            {
                return false;
            }
        }

        public override async Task<string> OpenFolderSelectorDialog(string defaultPath = "")
        {
            // TODO: Implement
            return string.Empty;
        }

        public override async Task<string> OpenFileSelectorDialog(string defaultPath = "", string defaultExt = "",
            string defaultFilter = "All files (*.*)|*.*")
        {
            // TODO: Implement
            return string.Empty;
        }
    }
}
