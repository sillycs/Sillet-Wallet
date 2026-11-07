// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
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
        bool SaveFile(string fileName, string filter, byte[] data);
        string PickOpenPath(string filter);
        System.Threading.Tasks.Task<Sillet.Hardware.ILedgerTransport> ConnectLedgerAsync();
        bool SharesFiles { get; }
        System.Threading.Tasks.Task<string> PickBackupFileAsync();
        System.Threading.Tasks.Task<byte[]> PickImageAsync();
        void ClearClipboardIf(string text);
        void ApplyTheme(IDictionary<string, string> colors);
        void SetCaptureProtection(bool on);
        bool IsAppActive();
        bool IsProcessHardened();
        void ShowSystemNotification(string title, string message, string kind = "info");
        void ApplyBackground(bool on);
        void RestartApp();
    }
}
