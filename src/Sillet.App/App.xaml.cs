// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.App
{
    public partial class App : Application, IUiHost
    {
        private static System.Threading.Mutex _instanceLock;
        private System.Threading.EventWaitHandle _showSignal;
        private bool _exiting;

        public bool IsExiting
        {
            get { return _exiting; }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (Sillet.App.Services.UpdateService.ApplyFromArguments(e.Args))
            {
                _exiting = true;
                Shutdown();
                return;
            }
            bool first;
            _instanceLock = new System.Threading.Mutex(true, "Sillet.SingleInstance." + Environment.UserName, out first);
            _showSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Sillet.ShowWindow." + Environment.UserName);
            if (!first)
            {
                _showSignal.Set();
                _exiting = true;
                Shutdown();
                return;
            }
            DispatcherUnhandledException += OnUnhandledException;
            SessionEnding += (s, args) => _exiting = true;
            ConfigureProtection();
            Sillet.App.Services.UpdateService.CleanupOld();
            Sillet.App.Services.UpdateService.AutoUpdateRequested += update => Dispatcher.BeginInvoke(new Action(() => { var ignored = RunAutoUpdateAsync(update); }));

            Sillet.App.Services.Loc.Enabled = true;
            Sillet.App.ViewModels.HomeViewModel.AlwaysShowAll = true;
            Sillet.App.Services.Sounds.Player = name => Dispatcher.BeginInvoke(new Action(() => PlaySound(name)));
            Sillet.App.Services.Sounds.Preloader = name => SoundFile(name);
            System.Threading.Tasks.Task.Run(() =>
            {
                Sillet.App.Services.Sounds.Preload();
                Dispatcher.BeginInvoke(new Action(WarmPlayer), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            });
            var viewModel = new MainViewModel(this);
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            bool background = Array.IndexOf(e.Args, "--background") >= 0 && viewModel.Settings.RunInBackground;
            if (!background) window.Show();
            StartBackgroundServices(viewModel.Settings);
        }

        private void StartBackgroundServices(AppSettings settings)
        {
            ApplyAutoStart(settings.StartWithSystem && settings.RunInBackground);
            if (settings.RunInBackground) EnsureTray();
            var watcher = PaymentWatcher.Instance;
            watcher.IsAppActive = () =>
            {
                try
                {
                    return Dispatcher.Invoke(new Func<bool>(IsAppActive));
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is TaskCanceledException)
                {
                    return false;
                }
            };
            watcher.Notifier = (title, message, kind) => Dispatcher.BeginInvoke(new Action(() => ShowSystemNotification(title, message, kind)));
            watcher.Start();
            var listener = new Thread(() =>
            {
                while (!_exiting)
                {
                    try
                    {
                        _showSignal.WaitOne();
                        if (_exiting) return;
                        Dispatcher.BeginInvoke(new Action(() => BringToFront()));
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            });
            listener.IsBackground = true;
            listener.Start();
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void ApplyAutoStart(bool on)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (on) key.SetValue("Sillet", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" --background");
                    else if (key.GetValue("Sillet") != null) key.DeleteValue("Sillet", false);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is System.IO.IOException)
            {
            }
        }

        public void RestartApp()
        {
            var path = Sillet.App.Services.UpdateService.RestartPath;
            if (path == null) return;
            try
            {
                var command = "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + path + "\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", command) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                return;
            }
            ExitApp();
        }

        private async Task RunAutoUpdateAsync(UpdateInfo update)
        {
            if (_exiting) return;
            var window = MainWindow;
            var wasVisible = window != null && window.IsVisible;
            if (wasVisible) window.Hide();
            var dialog = new Sillet.App.Views.UpdateWindow();
            dialog.Show();
            bool replace;
            try
            {
                replace = await dialog.RunAsync(update);
            }
            catch (Exception)
            {
                replace = false;
                dialog.Close();
            }
            if (replace)
            {
                dialog.Close();
                ExitApp();
                return;
            }
            if (dialog.MayRetry) Sillet.App.Services.UpdateService.AutoUpdateFinished();
            dialog.Closed += (s, e) =>
            {
                if (wasVisible && window != null && !_exiting) { window.Show(); window.Activate(); }
            };
            if (!dialog.IsVisible && wasVisible && window != null) { window.Show(); window.Activate(); }
        }

        public void ExitApp()
        {
            _exiting = true;
            try
            {
                if (_showSignal != null) _showSignal.Set();
            }
            catch (ObjectDisposedException)
            {
            }
            PaymentWatcher.Instance.Stop();
            var vm = MainWindow == null ? null : MainWindow.DataContext as MainViewModel;
            if (vm != null && vm.IsUnlocked) vm.Lock();
            if (_tray != null) _tray.Visible = false;
            Shutdown();
        }

        public void HideToTray()
        {
            var vm = MainWindow == null ? null : MainWindow.DataContext as MainViewModel;
            if (vm != null && vm.IsUnlocked) vm.Lock();
            EnsureTray();
            MainWindow.Hide();
            if (!_hintShown)
            {
                _hintShown = true;
                ShowSystemNotification("Sillet", Sillet.App.Services.Loc.T("Sillet keeps running in the background to tell you when you get paid"));
            }
        }

        private bool _hintShown;

        private readonly System.Collections.Generic.List<System.Windows.Media.MediaPlayer> _players = new System.Collections.Generic.List<System.Windows.Media.MediaPlayer>();

        private static readonly object SoundLock = new object();

        private static string SoundFile(string name)
        {
            lock (SoundLock)
            {
                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sillet-sounds");
                var path = System.IO.Path.Combine(folder, name + ".mp3");
                if (System.IO.File.Exists(path)) return path;
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Sillet.sound." + name))
                {
                    if (stream == null) return null;
                    System.IO.Directory.CreateDirectory(folder);
                    using (var file = System.IO.File.Create(path)) stream.CopyTo(file);
                }
                return path;
            }
        }

        private void WarmPlayer()
        {
            try
            {
                var path = SoundFile(Sillet.App.Services.Sounds.Unlocked);
                if (path == null) return;
                var player = new System.Windows.Media.MediaPlayer { Volume = 0 };
                player.MediaEnded += (s, e) => player.Close();
                player.MediaFailed += (s, e) => player.Close();
                player.Open(new Uri(path));
                player.Play();
            }
            catch (Exception)
            {
            }
        }

        private void PlaySound(string name)
        {
            try
            {
                var path = SoundFile(name);
                if (path == null) return;
                var player = new System.Windows.Media.MediaPlayer();
                player.MediaEnded += (s, e) => { player.Close(); _players.Remove(player); };
                player.MediaFailed += (s, e) => { player.Close(); _players.Remove(player); };
                _players.Add(player);
                player.Open(new Uri(path));
                player.Play();
            }
            catch (Exception)
            {
            }
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var vm = MainWindow == null ? null : MainWindow.DataContext as MainViewModel;
            if (vm == null) return;
            vm.ShowToast(MainViewModel.Friendly(e.Exception), true);
            e.Handled = true;
        }

        public void CopyToClipboard(string text)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetText(text, TextDataFormat.UnicodeText);
                    data.SetData("ExcludeClipboardContentFromMonitorProcessing", new System.IO.MemoryStream(new byte[] { 1, 0, 0, 0 }));
                    data.SetData("CanIncludeInClipboardHistory", new System.IO.MemoryStream(new byte[] { 0, 0, 0, 0 }));
                    data.SetData("CanUploadToCloudClipboard", new System.IO.MemoryStream(new byte[] { 0, 0, 0, 0 }));
                    Clipboard.SetDataObject(data, true);
                    return;
                }
                catch (COMException)
                {
                    Thread.Sleep(60);
                }
            }
        }

        public string ReadClipboard()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static readonly byte[] LocalEntropy = System.Text.Encoding.UTF8.GetBytes("Sillet.Passkey.v1");
        private static void ConfigureProtection()
        {
            Platform.WindowsProtection.Use();
            var settings = AppSettings.Load();
            Storage.Vault.LockToDevice = settings.LockVaultToDevice;
            if (settings.HardenProcess) Platform.ProcessGuard.Apply();
        }

        public bool IsProcessHardened()
        {
            return Platform.ProcessGuard.MemoryLocked;
        }

        private IntPtr WindowHandle()
        {
            return MainWindow == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(MainWindow).Handle;
        }

        public string PasskeyUnavailable()
        {
            return Platform.WindowsHello.Unavailable();
        }

        public Task<byte[]> PasskeyCreateAsync(byte[] secret)
        {
            var window = WindowHandle();
            return Task.Run(() => Platform.WindowsHello.Create(window, secret));
        }

        public Task<byte[]> PasskeyDecryptAsync(byte[] encrypted, string reason)
        {
            var window = WindowHandle();
            return Task.Run(() => Platform.WindowsHello.Decrypt(window, encrypted, reason));
        }

        public void PasskeyDelete()
        {
            Platform.WindowsHello.Delete();
        }

        public byte[] ProtectLocal(byte[] data)
        {
            return System.Security.Cryptography.ProtectedData.Protect(data, LocalEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        }

        public byte[] UnprotectLocal(byte[] data)
        {
            return System.Security.Cryptography.ProtectedData.Unprotect(data, LocalEntropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        }

        private System.Windows.Forms.NotifyIcon _tray;

        public bool IsAppActive()
        {
            return MainWindow != null && MainWindow.IsActive && MainWindow.WindowState != WindowState.Minimized;
        }

        private void EnsureTray()
        {
            if (_tray != null)
            {
                _tray.Visible = true;
                return;
            }
            _tray = new System.Windows.Forms.NotifyIcon { Text = "Sillet" };
            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/silleticon.ico", UriKind.Absolute));
            if (info != null)
                using (var stream = info.Stream) _tray.Icon = new System.Drawing.Icon(stream);
            else _tray.Icon = System.Drawing.SystemIcons.Application;
            _tray.BalloonTipClicked += (s, e) => BringToFront(_balloonTarget);
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) BringToFront();
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(Sillet.App.Services.Loc.T("Open Sillet"), null, (s, e) => BringToFront());
            var auto = new System.Windows.Forms.ToolStripMenuItem(Sillet.App.Services.Loc.T("Start with Windows"));
            auto.CheckOnClick = true;
            auto.Checked = AppSettings.Load().StartWithSystem;
            auto.Click += (s, e) =>
            {
                var settings = AppSettings.Load();
                settings.StartWithSystem = auto.Checked;
                settings.Save();
                ApplyAutoStart(auto.Checked);
            };
            menu.Items.Add(auto);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(Sillet.App.Services.Loc.T("Exit"), null, (s, e) => ExitApp());
            menu.Opening += (s, e) => TrayTheme.Apply(menu);
            TrayTheme.Apply(menu);
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
        }

        public void ApplyBackground(bool on)
        {
            ApplyAutoStart(on && AppSettings.Load().StartWithSystem);
            if (on) EnsureTray();
        }

        public void ShowSystemNotification(string title, string message, string kind = "info")
        {
            try
            {
                EnsureTray();
                _balloonTarget = kind == "payment" ? "activity" : kind == "security" || kind == "update" ? "settings" : null;
                _tray.ShowBalloonTip(6000, title, string.IsNullOrEmpty(message) ? title : message, System.Windows.Forms.ToolTipIcon.None);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is System.IO.IOException || e is ArgumentException || e is InvalidOperationException)
            {
            }
        }

        private string _balloonTarget;

        private void BringToFront(string target = null)
        {
            if (MainWindow == null || _exiting) return;
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Show();
            MainWindow.Activate();
            var vm = MainWindow.DataContext as MainViewModel;
            if (vm == null) return;
            if (!string.IsNullOrEmpty(target)) vm.OpenTarget(target);
            else if (vm.IsUnlocked) vm.IsNotificationsOpen = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            base.OnExit(e);
        }

        public void SetCaptureProtection(bool on)
        {
            var window = MainWindow as MainWindow;
            if (window != null) window.SetCaptureProtection(on);
        }

        public void ApplyTheme(IDictionary<string, string> colors)
        {
            var window = MainWindow as MainWindow;
            if (window != null) window.CrossFade(() => SetColors(colors));
            else SetColors(colors);
        }

        private void SetColors(IDictionary<string, string> colors)
        {
            foreach (var pair in colors)
            {
                Color color;
                try
                {
                    color = (Color)ColorConverter.ConvertFromString(pair.Value);
                }
                catch (FormatException)
                {
                    continue;
                }
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Resources[pair.Key] = brush;
            }
        }

        public string ScanQrFromFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a QR code image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(MainWindow) != true) return null;
            return QrReader.FromFile(dialog.FileName);
        }

        public string ScanQrFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage()) return QrReader.Decode(Clipboard.GetImage());
                if (Clipboard.ContainsFileDropList())
                {
                    foreach (var file in Clipboard.GetFileDropList()) return QrReader.FromFile(file);
                }
            }
            catch (COMException)
            {
            }
            return null;
        }

        public string PickSavePath(string fileName, string filter)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = filter, OverwritePrompt = true };
            return dialog.ShowDialog(MainWindow) == true ? dialog.FileName : null;
        }

        public bool SaveFile(string fileName, string filter, byte[] data)
        {
            var path = PickSavePath(fileName, filter);
            if (path == null) return false;
            System.IO.File.WriteAllBytes(path, data);
            return true;
        }

        public bool SharesFiles { get { return false; } }

        public Task<Sillet.Hardware.ILedgerTransport> ConnectLedgerAsync()
        {
            return Sillet.App.Services.LedgerHid.OpenAsync();
        }

        public Task<string> PickBackupFileAsync()
        {
            return Task.FromResult(PickOpenPath("Sillet backup|*.json|All files|*.*"));
        }

        public string PickOpenPath(string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
            return dialog.ShowDialog(MainWindow) == true ? dialog.FileName : null;
        }

        public Task<byte[]> PickImageAsync()
        {
            var path = PickOpenPath("Pictures|*.png;*.jpg;*.jpeg");
            try
            {
                if (path == null || new System.IO.FileInfo(path).Length > Sillet.Storage.AvatarStore.MaxBytes) return Task.FromResult<byte[]>(null);
                return Task.FromResult(System.IO.File.ReadAllBytes(path));
            }
            catch (System.IO.IOException)
            {
                return Task.FromResult<byte[]>(null);
            }
        }

        public void ClearClipboardIf(string text)
        {
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == text) Clipboard.Clear();
            }
            catch (COMException)
            {
            }
        }

        public void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

        public void OpenUrl(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
