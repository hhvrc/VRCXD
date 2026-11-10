using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VRCX
{
    public partial class AppApiCef
    {
        public override string GetVRChatAppDataLocation()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"Low\VRChat\VRChat";
        }

        protected override string GetDefaultVRChatPhotosLocation()
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VRChat");
        }

        protected override string GetVRChatCrashDumpsLocation()
        {
            return Path.Join(Path.GetTempPath(), "VRChat", "VRChat", "Crashes");
        }

        private string GetSteamUserdataPathFromRegistry()
        {
            string steamUserdataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\userdata");

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                    {
                        object o = key.GetValue("InstallPath");
                        if (o != null)
                        {
                            steamUserdataPath = Path.Join(o.ToString(), @"userdata");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"Failed to get Steam userdata path from registry: {e}");
            }

            return steamUserdataPath;
        }

        public override string GetVRChatScreenshotsLocation()
        {
            // program files steam userdata screenshots
            var steamUserdataPath = GetSteamUserdataPathFromRegistry();
            var screenshotPath = string.Empty;
            var latestWriteTime = DateTime.MinValue;
            if (!Directory.Exists(steamUserdataPath))
                return screenshotPath;

            var steamUserDirs = Directory.GetDirectories(steamUserdataPath);
            foreach (var steamUserDir in steamUserDirs)
            {
                var screenshotDir = Path.Join(steamUserDir, @"760\remote\438100\screenshots");
                if (!Directory.Exists(screenshotDir))
                    continue;

                var lastWriteTime = File.GetLastWriteTime(screenshotDir);
                if (lastWriteTime <= latestWriteTime)
                    continue;

                latestWriteTime = lastWriteTime;
                screenshotPath = screenshotDir;
            }

            return screenshotPath;
        }

        public override void OpenFolderAndSelectItem(string path, bool isFolder = false)
        {
            path = Path.GetFullPath(path);
            // I don't think it's quite meant for it, but SHOpenFolderAndSelectItems can open folders by passing the folder path as the item to select, as a child to itself, somehow. So we'll check to see if 'path' is a folder as well.
            if (!File.Exists(path) && !Directory.Exists(path))
                return;

            var folderPath = isFolder ? path : Path.GetDirectoryName(path);
            IntPtr pidlFolder;
            IntPtr pidlFile;
            uint psfgaoOut;

            // Convert our managed strings to PIDLs. PIDLs are essentially pointers to the actual file system objects, separate from the "display name", which is the human-readable path to the file/folder. We're parsing the display name into a PIDL here.
            // The windows shell uses PIDLs to identify objects in winapi calls, so we'll need to use them to open the folder and select the file. Cool stuff!
            var result = WinApi.SHParseDisplayName(folderPath, IntPtr.Zero, out pidlFolder, 0, out psfgaoOut);
            if (result != 0)
            {
                OpenFolderAndSelectItemFallback(path);
                return;
            }

            result = WinApi.SHParseDisplayName(path, IntPtr.Zero, out pidlFile, 0, out psfgaoOut);
            if (result != 0)
            {
                // Free the PIDL we allocated earlier if we failed to parse the display name of the file.
                Marshal.FreeCoTaskMem(pidlFolder);
                OpenFolderAndSelectItemFallback(path);
                return;
            }

            IntPtr[] files = { pidlFile };

            try
            {
                // Open the containing folder and select our file. SHOpenFolderAndSelectItems will respect existing explorer instances, open a new one if none exist, will properly handle paths > 120 chars, and work with third-party filesystem viewers that hook into winapi calls.
                // It can select multiple items, but we only need to select one. 
                WinApi.SHOpenFolderAndSelectItems(pidlFolder, (uint)files.Length, files, 0);
            }
            catch
            {
                OpenFolderAndSelectItemFallback(path);
            }
            finally
            {
                // Free the PIDLs we allocated earlier
                Marshal.FreeCoTaskMem(pidlFolder);
                Marshal.FreeCoTaskMem(pidlFile);
            }
        }

        private void OpenFolderAndSelectItemFallback(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return;

            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                // open folder with file highlighted
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }

        public override async Task<string> OpenFolderSelectorDialog(string defaultPath = "")
        {
            var tcs = new TaskCompletionSource<string>();
            var staThread = new Thread(() =>
            {
                try
                {
                    using var openFolderDialog = new FolderBrowserDialog();
                    openFolderDialog.InitialDirectory = Directory.Exists(defaultPath) ? defaultPath : GetVRChatPhotosLocation();

                    var dialogResult = openFolderDialog.ShowDialog(MainForm.nativeWindow);
                    if (dialogResult == DialogResult.OK)
                    {
                        tcs.SetResult(openFolderDialog.SelectedPath);
                    }
                    else
                    {
                        tcs.SetResult(defaultPath);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            return await tcs.Task;
        }

        public override async Task<string> OpenFileSelectorDialog(string defaultPath = "", string defaultExt = "", string defaultFilter = "All files (*.*)|*.*")
        {
            var tcs = new TaskCompletionSource<string>();
            var staThread = new Thread(() =>
            {
                try
                {
                    using (var openFileDialog = new System.Windows.Forms.OpenFileDialog())
                    {
                        if (Directory.Exists(defaultPath))
                        {
                            openFileDialog.InitialDirectory = defaultPath;
                        }

                        openFileDialog.DefaultExt = defaultExt;
                        openFileDialog.Filter = defaultFilter;

                        var dialogResult = openFileDialog.ShowDialog(MainForm.nativeWindow);
                        if (dialogResult == DialogResult.OK && !string.IsNullOrEmpty(openFileDialog.FileName))
                        {
                            tcs.SetResult(openFileDialog.FileName);
                        }
                        else
                        {
                            tcs.SetResult("");
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            return await tcs.Task;
        }
    }
}
