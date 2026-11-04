using System;
using Sillet.App.Services;

namespace Sillet.Mobile
{
    public static class MobilePlatform
    {
        public static Action<bool> SetSecure { get; set; }
        public static Action<string, byte[]> PlaySound { get; set; }
        public static Action<string, string> ShareText { get; set; }
        public static Action<string, string, byte[]> ShareImage { get; set; }
        public static Action<string, string, byte[], string> ShareFile { get; set; }
        public static Action<string, string, string> Notify { get; set; }
        public static Action<bool> SetBackground { get; set; }
        public static bool StartHidden { get; set; }
        public static bool Exiting { get; set; }
        public static Func<bool> IsActive { get; set; }
        public static Func<double[]> GetSafeArea { get; set; }
        public static Func<string> PasskeyUnavailable { get; set; }
        public static Func<byte[], System.Threading.Tasks.Task<byte[]>> PasskeyCreate { get; set; }
        public static Func<byte[], string, System.Threading.Tasks.Task<byte[]>> PasskeyDecrypt { get; set; }
        public static Action PasskeyDelete { get; set; }
        public static Func<bool, System.Threading.Tasks.Task<string>> ScanQr { get; set; }
        public static Action<string> CopyText { get; set; }
        public static Action<string> ClearClipboardIf { get; set; }
        public static event Action ScreenshotTaken;
        public static event Action NotificationsAllowed;
        public static event Action<string> OpenTargetRequested;
        public static string PendingTarget { get; set; }

        /// <summary>Android reads the update notice from its own file and compares with its own version number.</summary>
        public static void ConfigureUpdates(string version)
        {
            if (!string.IsNullOrEmpty(version)) AppInfo.CurrentVersion = version;
            AppInfo.VersionUrl = AppInfo.AndroidVersionUrl;
        }

        /// <summary>A tapped notification asked for a screen. Kept until a view picks it up.</summary>
        public static void RequestTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            PendingTarget = target;
            var handler = OpenTargetRequested;
            if (handler != null) handler(target);
        }

        public static void RaiseNotificationsAllowed()
        {
            var handler = NotificationsAllowed;
            if (handler != null) handler();
        }

        public static bool CanShare => ShareText != null;

        public static void RaiseScreenshot()
        {
            var handler = ScreenshotTaken;
            if (handler != null) handler();
        }
    }
}
