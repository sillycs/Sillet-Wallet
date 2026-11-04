using System.Collections.Generic;

namespace Sillet.App.Services
{
    public interface IUiHost : IPasskeyHost
    {
        void CopyToClipboard(string text);
        string ReadClipboard();
        void OpenUrl(string url);
        void OpenFolder(string path);
        string ScanQrFromFile();
        string ScanQrFromClipboard();
        string PickSavePath(string fileName, string filter);
        /// <summary>Saves or shares a file the user asked to download. Returns false when they cancelled.</summary>
        bool SaveFile(string fileName, string filter, byte[] data);
        string PickOpenPath(string filter);
        /// <summary>Lets the user pick a picture (PNG or JPEG) and returns its bytes, or null when cancelled or too big.</summary>
        System.Threading.Tasks.Task<byte[]> PickImageAsync();
        void ClearClipboardIf(string text);
        void ApplyTheme(IDictionary<string, string> colors);
        void SetCaptureProtection(bool on);
        bool IsAppActive();
        bool IsProcessHardened();
        void ShowSystemNotification(string title, string message, string kind = "info");
        void ApplyBackground(bool on);
        /// <summary>Closes Sillet and opens it again (used after an update). Does nothing where that is not possible.</summary>
        void RestartApp();
    }
}
