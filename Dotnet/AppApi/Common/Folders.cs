using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRCX
{
    public partial class AppApi
    {
        /// <summary>
        /// Platform default for the VRChat photos folder, used when config.json does not set picture_output_folder.
        /// </summary>
        protected abstract string GetDefaultVRChatPhotosLocation();

        /// <summary>
        /// Platform location of the VRChat crash dumps folder.
        /// </summary>
        protected abstract string GetVRChatCrashDumpsLocation();

        public string GetVRChatCacheLocation()
        {
            var defaultPath = Path.Join(GetVRChatAppDataLocation(), "Cache-WindowsPlayer");
            try
            {
                var json = ReadConfigFile();
                if (string.IsNullOrEmpty(json))
                    return defaultPath;

                var obj = JsonConvert.DeserializeObject<JObject>(json, JsonSerializerSettings);
                if (obj["cache_directory"] == null)
                    return defaultPath;

                var cacheDir = (string)obj["cache_directory"];
                if (string.IsNullOrEmpty(cacheDir))
                    return defaultPath;

                var cachePath = Path.Join(cacheDir, "Cache-WindowsPlayer");
                if (!Directory.Exists(cacheDir))
                    return defaultPath;

                return cachePath;
            }
            catch (Exception e)
            {
                logger.Error($"Error reading VRChat config file for cache location: {e}");
            }
            return defaultPath;
        }

        public string GetVRChatPhotosLocation()
        {
            var defaultPath = GetDefaultVRChatPhotosLocation();
            try
            {
                var json = ReadConfigFile();
                if (string.IsNullOrEmpty(json))
                    return defaultPath;

                var obj = JsonConvert.DeserializeObject<JObject>(json, JsonSerializerSettings);
                if (obj["picture_output_folder"] == null)
                    return defaultPath;

                var photosDir = (string)obj["picture_output_folder"];
                if (string.IsNullOrEmpty(photosDir) || !Directory.Exists(photosDir))
                    return defaultPath;

                return photosDir;
            }
            catch (Exception e)
            {
                logger.Error($"Error reading VRChat config file for photos location: {e}");
            }
            return defaultPath;
        }

        public string GetUGCPhotoLocation(string path = "")
        {
            if (string.IsNullOrEmpty(path))
            {
                return GetVRChatPhotosLocation();
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            catch (Exception e)
            {
                logger.Error(e);
                return GetVRChatPhotosLocation();
            }
        }

        /// <summary>
        /// Opens the folder in the platform file manager if it exists.
        /// </summary>
        /// <returns>false if the folder does not exist</returns>
        private bool OpenFolderIfExists(string path)
        {
            if (!Directory.Exists(path))
                return false;

            OpenFolderAndSelectItem(path, true);
            return true;
        }

        public bool OpenVrcxAppDataFolder()
        {
            return OpenFolderIfExists(Program.AppDataDirectory);
        }

        public bool OpenVrcAppDataFolder()
        {
            return OpenFolderIfExists(GetVRChatAppDataLocation());
        }

        public bool OpenVrcPhotosFolder()
        {
            return OpenFolderIfExists(GetVRChatPhotosLocation());
        }

        public bool OpenUGCPhotosFolder(string ugcPath = "")
        {
            return OpenFolderIfExists(GetUGCPhotoLocation(ugcPath));
        }

        public bool OpenVrcScreenshotsFolder()
        {
            return OpenFolderIfExists(GetVRChatScreenshotsLocation());
        }

        public bool OpenCrashVrcCrashDumps()
        {
            return OpenFolderIfExists(GetVRChatCrashDumpsLocation());
        }

        public void OpenShortcutFolder()
        {
            OpenFolderIfExists(AutoAppLaunchManager.Instance.AppShortcutDirectory);
        }
    }
}
