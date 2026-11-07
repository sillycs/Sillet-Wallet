// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Sillet.App.Services;

namespace Sillet.Mobile.UI
{
    public sealed class MobileHost : IUiHost
    {
        private const string NoPasskey = "Fingerprint unlock is coming soon to the mobile app";
        private readonly Application _app;

        public MobileHost(Application app)
        {
            _app = app;
        }

        public Control View { get; set; }

        private TopLevel Top => View == null ? null : TopLevel.GetTopLevel(View);

        public void CopyToClipboard(string text)
        {
            var native = MobilePlatform.CopyText;
            if (native != null)
            {
                native(text);
                return;
            }
            var clipboard = Top == null ? null : Top.Clipboard;
            if (clipboard != null) _ = clipboard.SetTextAsync(text);
        }

        public string ReadClipboard()
        {
            return null;
        }

        public async Task<string> ReadClipboardAsync()
        {
            var clipboard = Top == null ? null : Top.Clipboard;
            return clipboard == null ? null : await clipboard.GetTextAsync();
        }

        private static readonly string[] External = { "t.me", "telegram.me", "discord.gg", "discord.com", "moonpay.com", "ramp.network", "transak.com", "buda.com", "orionx.com", "exolix.com" };

        private static bool UsesInAppBrowser(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
            foreach (var host in External)
                if (uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public void OpenUrl(string url)
        {
            var top = Top;
            if (top == null || string.IsNullOrEmpty(url)) return;
            if (MobilePlatform.OpenBrowser != null && UsesInAppBrowser(url))
            {
                MobilePlatform.OpenBrowser(url);
                return;
            }
            _ = top.Launcher.LaunchUriAsync(new Uri(url));
        }

        public void OpenFolder(string path)
        {
        }

        public string ScanQrFromFile()
        {
            return null;
        }

        public string ScanQrFromClipboard()
        {
            return null;
        }

        public string PickSavePath(string fileName, string filter)
        {
            return null;
        }

        public bool SaveFile(string fileName, string filter, byte[] data)
        {
            if (MobilePlatform.ShareFile != null)
            {
                MobilePlatform.ShareFile(fileName, fileName, data, fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/pdf");
                return true;
            }
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!System.IO.Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) folder = System.IO.Path.GetTempPath();
            var path = System.IO.Path.Combine(folder, fileName);
            System.IO.File.WriteAllBytes(path, data);
            return true;
        }

        public Task<Sillet.Hardware.ILedgerTransport> ConnectLedgerAsync()
        {
            var connect = MobilePlatform.ConnectLedger;
            return connect == null ? Task.FromResult<Sillet.Hardware.ILedgerTransport>(null) : connect();
        }

        public bool SharesFiles { get { return MobilePlatform.ShareFile != null; } }

        public async Task<string> PickBackupFileAsync()
        {
            var top = Top;
            if (top == null) return null;
            try
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false });
                if (files == null || files.Count == 0) return null;
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sillet-restore.json");
                using (var input = await files[0].OpenReadAsync())
                using (var output = System.IO.File.Create(path))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > 1024 * 1024) return null;
                        output.Write(buffer, 0, read);
                    }
                }
                return path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string PickOpenPath(string filter)
        {
            return null;
        }

        public async Task<byte[]> PickImageAsync()
        {
            var top = Top;
            if (top == null) return null;
            try
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
                });
                if (files == null || files.Count == 0) return null;
                using (var stream = await files[0].OpenReadAsync())
                using (var memory = new System.IO.MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        memory.Write(buffer, 0, read);
                        if (memory.Length > Sillet.Storage.AvatarStore.MaxBytes) return null;
                    }
                    return memory.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async void ClearClipboardIf(string text)
        {
            var native = MobilePlatform.ClearClipboardIf;
            if (native != null)
            {
                native(text);
                return;
            }
            var clipboard = Top == null ? null : Top.Clipboard;
            if (clipboard == null) return;
            var current = await clipboard.GetTextAsync();
            if (current == text) await clipboard.ClearAsync();
        }

        public void ApplyTheme(IDictionary<string, string> colors)
        {
            foreach (var pair in colors)
            {
                Color color;
                if (!Color.TryParse(pair.Value, out color)) continue;
                _app.Resources[pair.Key] = new SolidColorBrush(color);
                if (pair.Key == "BgBrush")
                {
                    var light = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 140;
                    _app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
                }
            }
        }

        public void SetCaptureProtection(bool on)
        {
            var action = MobilePlatform.SetSecure;
            if (action != null) action(on);
        }

        public bool IsAppActive()
        {
            var check = MobilePlatform.IsActive;
            return check == null || check();
        }

        public bool IsProcessHardened()
        {
            return false;
        }

        public void ShowSystemNotification(string title, string message, string kind = "info")
        {
            var notify = MobilePlatform.Notify;
            if (notify != null) notify(title, message, kind);
        }

        public void RestartApp()
        {
        }

        public void ApplyBackground(bool on)
        {
            var apply = MobilePlatform.SetBackground;
            if (apply != null) apply(on);
        }

        public string PasskeyUnavailable()
        {
            var check = MobilePlatform.PasskeyUnavailable;
            return check == null ? Loc.T(NoPasskey) : check();
        }

        public Task<byte[]> PasskeyCreateAsync(byte[] secret)
        {
            var create = MobilePlatform.PasskeyCreate;
            if (create == null) throw new PasskeyException(Loc.T(NoPasskey), false);
            return create(secret);
        }

        public Task<byte[]> PasskeyDecryptAsync(byte[] encrypted, string reason)
        {
            var decrypt = MobilePlatform.PasskeyDecrypt;
            if (decrypt == null) throw new PasskeyException(Loc.T(NoPasskey), false, true);
            return decrypt(encrypted, reason);
        }

        public void PasskeyDelete()
        {
            var delete = MobilePlatform.PasskeyDelete;
            if (delete != null) delete();
        }

        public byte[] ProtectLocal(byte[] data)
        {
            return data;
        }

        public byte[] UnprotectLocal(byte[] data)
        {
            return data;
        }
    }
}
