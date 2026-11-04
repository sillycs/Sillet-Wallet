using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Sillet.App.ViewModels;

namespace Sillet.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StateChanged += OnStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
            Closing += OnClosing;
            PreviewMouseDown += (s, e) => Touch();
            PreviewMouseMove += (s, e) => Touch();
            PreviewMouseWheel += (s, e) => Touch();
            PreviewTextInput += (s, e) => Touch();
            Activated += (s, e) => SetPrivacyBlur(false);
            Deactivated += (s, e) => SetPrivacyBlur(true);
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Loaded += OnSplashLoaded;
        }

        private void OnSplashLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnSplashLoaded;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (s2, a2) => Splash.Visibility = Visibility.Collapsed;
                Splash.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }

        private void SetPrivacyBlur(bool on)
        {
            var vm = DataContext as MainViewModel;
            var blur = on && vm != null && vm.IsUnlocked && vm.Settings.BlurWhenInactive;
            if (!blur)
            {
                PageHost.Effect = null;
                return;
            }
            var effect = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian };
            PageHost.Effect = effect;
            effect.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 22, TimeSpan.FromMilliseconds(220)));
        }

        private void LockFromSystem()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var vm = DataContext as MainViewModel;
                if (vm != null && vm.IsUnlocked && vm.Settings.LockOnSystemLock) vm.Lock();
            }));
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock || e.Reason == Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
                || e.Reason == Microsoft.Win32.SessionSwitchReason.RemoteDisconnect || e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLogoff)
                LockFromSystem();
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend) LockFromSystem();
        }

        private const uint WdaNone = 0;
        private const uint WdaMonitor = 1;
        private const uint WdaExcludeFromCapture = 0x11;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        public void SetCaptureProtection(bool on)
        {
            if (_hwnd == null) return;
            if (!on)
            {
                SetWindowDisplayAffinity(_hwnd.Handle, WdaNone);
                return;
            }
            if (!SetWindowDisplayAffinity(_hwnd.Handle, WdaExcludeFromCapture)) SetWindowDisplayAffinity(_hwnd.Handle, WdaMonitor);
        }

        private void Touch()
        {
            var vm = DataContext as MainViewModel;
            if (vm != null) vm.Touch();
        }

        public void CrossFade(Action apply)
        {
            var width = RootGrid.ActualWidth;
            var height = RootGrid.ActualHeight;
            if (!IsLoaded || width < 1 || height < 1)
            {
                apply();
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var background = TryFindResource("BgBrush") as Brush ?? Brushes.Black;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, width, height);
                dc.DrawRectangle(background, null, rect);
                dc.DrawRectangle(new VisualBrush(RootGrid) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
            }
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            ThemeFade.BeginAnimation(OpacityProperty, null);
            ThemeFade.Source = bitmap;
            ThemeFade.Opacity = 1;
            ThemeFade.Visibility = Visibility.Visible;

            apply();

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            };
            fade.Completed += (s, e) =>
            {
                if (ThemeFade.Source != bitmap) return;
                ThemeFade.Visibility = Visibility.Collapsed;
                ThemeFade.Source = null;
            };
            ThemeFade.BeginAnimation(OpacityProperty, fade);
        }

        private const int WmClipboardUpdate = 0x031D;
        private HwndSource _hwnd;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = PresentationSource.FromVisual(this) as HwndSource;
            if (_hwnd == null) return;
            _hwnd.AddHook(WndProc);
            AddClipboardFormatListener(_hwnd.Handle);
            var vm = DataContext as MainViewModel;
            if (vm != null && vm.Settings.BlockScreenCapture) SetCaptureProtection(true);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var app = Application.Current as App;
            var vm = DataContext as MainViewModel;
            if (app == null || app.IsExiting) return;
            if (vm != null && vm.Settings.RunInBackground)
            {
                e.Cancel = true;
                app.HideToTray();
                return;
            }
            e.Cancel = true;
            app.ExitApp();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_hwnd == null) return;
            RemoveClipboardFormatListener(_hwnd.Handle);
            _hwnd.RemoveHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmClipboardUpdate)
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                {
                    string text = null;
                    try
                    {
                        if (Clipboard.ContainsText()) text = Clipboard.GetText();
                    }
                    catch (COMException)
                    {
                    }
                    if (text != null) vm.OnClipboardChanged(text);
                }
            }
            return IntPtr.Zero;
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            RootGrid.Margin = maximized ? new Thickness(7) : new Thickness(0);
            MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
            var vm = DataContext as MainViewModel;
            if (WindowState == WindowState.Minimized && vm != null && vm.IsUnlocked && vm.Settings.LockOnMinimize) vm.Lock();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            Touch();
            var vm = DataContext as MainViewModel;
            if (vm != null && HandleShortcut(vm, e))
            {
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && vm != null && vm.IsNotificationsOpen)
            {
                vm.IsNotificationsOpen = false;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && vm != null && vm.IsDialogOpen)
            {
                vm.DialogCancelCommand.Execute(null);
                e.Handled = true;
            }
        }

        private static readonly string[] Pages = { "home", "market", "send", "receive", "trade", "activity", "settings" };

        private static bool HandleShortcut(MainViewModel vm, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || !vm.IsUnlocked || vm.IsDialogOpen) return false;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            int index = -1;
            if (key >= Key.D1 && key <= Key.D7) index = key - Key.D1;
            else if (key >= Key.NumPad1 && key <= Key.NumPad7) index = key - Key.NumPad1;
            if (index >= 0)
            {
                vm.Navigate(Pages[index]);
                return true;
            }
            switch (key)
            {
                case Key.L:
                    vm.Lock();
                    return true;
                case Key.B:
                    vm.ToggleNavCommand.Execute(null);
                    return true;
                case Key.R:
                    if (vm.RefreshCommand.CanExecute(null)) vm.RefreshCommand.Execute(null);
                    return true;
                case Key.H:
                    vm.ToggleHideCommand.Execute(null);
                    return true;
                case Key.K:
                    vm.Navigate("market");
                    if (vm.Markets != null) vm.Markets.RequestSearchFocus();
                    return true;
                default:
                    return false;
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }
    }
}
