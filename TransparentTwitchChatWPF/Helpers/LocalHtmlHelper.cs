using System;
using System.IO;
using System.Text.RegularExpressions;
using TwitchLib.Api.Helix;

namespace TransparentTwitchChatWPF.Helpers
{
    internal static class LocalHtmlHelper
    {   
        private static readonly string BrowserBasePath = Path.Combine(
            AppContext.BaseDirectory, "browser");
        
        public static string GetIndexHtmlPath()
        {
            return Path.Combine(BrowserBasePath, "index.html");
        }
        
        public static string GetJChatIndexPath()
        {
            return Path.Combine(BrowserBasePath, "jchat.html");
        }
    }

    /// <summary>
    /// Provides paths to local overlay HTML files. NativeChat is managed as a
    /// writable asset pack so installed builds can update or repair it without
    /// modifying the application content directory.
    /// </summary>
    internal static class OverlayPathHelper
    {
        /// <summary>
        /// The base path to the "browser" directory within the application's folder.
        /// </summary>
        private static readonly string BrowserBasePath = Path.Combine(AppContext.BaseDirectory, "browser");

        private static readonly string NativeChatBasePath = GetNativeChatBasePath();

        /// <summary>
        /// Gets the full, absolute path to the settings page for the Native Chat overlay.
        /// </summary>
        /// <returns>The full path to native-chat\index.html.</returns>
        public static string GetNativeChatSettingsIndexFilePath()
        {
            return Path.Combine(NativeChatBasePath, "index.html");
        }

        /// <summary>
        /// Gets the full, absolute path to the Native Chat overlay.
        /// </summary>
        /// <returns>The full path to native-chat.</returns>
        public static string GetNativeChatPath()
        {
            return NativeChatBasePath;
        }

        /// <summary>
        /// Gets the full, absolute path to the actual chat overlay for the Native Chat overlay.
        /// </summary>
        /// <returns>The full path to native-chat\v2\index.html.</returns>
        public static string GetNativeChatOverlayPath()
        {
            return Path.Combine(NativeChatBasePath, "v2", "index.html");
        }

        public static string GetNativeChatHostname()
        {
            return "nativechat.overlay";
        }

        /// <summary>
        /// Checks if a given overlay's base directory exists.
        /// </summary>
        /// <param name="overlayId">The ID (folder name) of the overlay.</param>
        /// <returns>True if the directory exists, otherwise false.</returns>
        public static bool DoesOverlayExist(string overlayId)
        {
            if (string.IsNullOrEmpty(overlayId)) return false;

            if (string.Equals(overlayId, "native-chat", StringComparison.OrdinalIgnoreCase))
                return Directory.Exists(GetNativeChatPath());

            string overlayPath = Path.Combine(BrowserBasePath, "overlays", overlayId);
            return Directory.Exists(overlayPath);
        }

        private static string GetNativeChatBasePath()
        {
            if (AppInfo.IsPortable)
                return Path.Combine(BrowserBasePath, "overlays", "native-chat");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TransparentTwitchChatWPF",
                "NativeChat",
                "active");
        }
    }
}
