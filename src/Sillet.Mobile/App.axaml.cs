using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Sillet.App.Services;
using Sillet.App.ViewModels;
using Sillet.Mobile.UI;

namespace Sillet.Mobile
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            MobileStrings.Register();
            MobileDefaults.Apply();
            Sillet.App.Services.Loc.Enabled = true;
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var host = new MobileHost(this);
            var soundBytes = new System.Collections.Generic.Dictionary<string, byte[]>();
            Sillet.App.Services.Sounds.Player = name =>
            {
                var play = MobilePlatform.PlaySound;
                if (play == null) return;
                byte[] bytes;
                if (!soundBytes.TryGetValue(name, out bytes))
                {
                    using (var stream = AssetLoader.Open(new System.Uri("avares://Sillet.Mobile/Assets/sounds/" + name + ".mp3")))
                    using (var memory = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(memory);
                        bytes = memory.ToArray();
                    }
                    soundBytes[name] = bytes;
                }
                play(name, bytes);
            };
            var view = new MainView();
            host.View = view;
            view.DataContext = new MainViewModel(host);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                SetUpDesktop(desktop, view);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            {
                single.MainView = view;
            }
            base.OnFrameworkInitializationCompleted();
        }

        private static Window _window;
        private static volatile bool _windowActive;
        private TrayIcon _tray;

        private void SetUpDesktop(IClassicDesktopStyleApplicationLifetime desktop, Control view)
        {
            var settings = AppSettings.Load();
            var window = new Window
            {
                Title = "Sillet",
                Width = 430,
                Height = 860,
                Content = view,
            };
            try
            {
                using (var stream = AssetLoader.Open(new System.Uri("avares://Sillet.Mobile/Assets/silleticon.png")))
                    window.Icon = new WindowIcon(stream);
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.ArgumentException || e is System.InvalidOperationException)
            {
            }
            _window = window;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.Activated += (s, e) => _windowActive = true;
            window.Deactivated += (s, e) => _windowActive = false;
            window.Closing += (s, e) =>
            {
                if (MobilePlatform.Exiting) return;
                var vm = view.DataContext as MainViewModel;
                if (vm != null && vm.Settings.RunInBackground)
                {
                    e.Cancel = true;
                    if (vm.IsUnlocked) vm.Lock();
                    _windowActive = false;
                    window.Hide();
                    return;
                }
                MobilePlatform.Exiting = true;
                PaymentWatcher.Instance.Stop();
                desktop.Shutdown();
            };
            MobilePlatform.IsActive = () => _windowActive;
            MobilePlatform.SetBackground = on => Dispatcher.UIThread.Post(() => ApplyBackground(on, desktop));

            var hidden = MobilePlatform.StartHidden && settings.RunInBackground;
            if (!hidden) window.Show();
            ApplyBackground(settings.RunInBackground, desktop);
            PaymentWatcher.Instance.IsAppActive = () => _windowActive;
            PaymentWatcher.Instance.Notifier = (title, message, kind) =>
            {
                var notify = MobilePlatform.Notify;
                if (notify != null) notify(title, message, kind);
            };
        }

        private void ApplyBackground(bool on, IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!on)
            {
                if (_tray != null) _tray.IsVisible = false;
                PaymentWatcher.Instance.Stop();
                return;
            }
            if (_tray == null)
            {
                var open = new NativeMenuItem(Loc.T("Open Sillet"));
                open.Click += (s, e) => ShowWindow();
                var exit = new NativeMenuItem(Loc.T("Exit"));
                exit.Click += (s, e) =>
                {
                    MobilePlatform.Exiting = true;
                    PaymentWatcher.Instance.Stop();
                    desktop.Shutdown();
                };
                var menu = new NativeMenu();
                menu.Items.Add(open);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(exit);
                _tray = new TrayIcon { ToolTipText = "Sillet", Menu = menu };
                try
                {
                    using (var stream = AssetLoader.Open(new System.Uri("avares://Sillet.Mobile/Assets/silleticon.png")))
                        _tray.Icon = new WindowIcon(stream);
                }
                catch (System.Exception e) when (e is System.IO.IOException || e is System.ArgumentException || e is System.InvalidOperationException)
                {
                }
                _tray.Clicked += (s, e) => ShowWindow();
                TrayIcon.SetIcons(this, new TrayIcons { _tray });
            }
            _tray.IsVisible = true;
            PaymentWatcher.Instance.Start();
        }

        private static void ShowWindow()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_window == null) return;
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            });
        }
    }
}
