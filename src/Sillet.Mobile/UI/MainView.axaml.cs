// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Sillet.App.Services;
using Sillet.App.ViewModels;
using Sillet.Mobile.UI.Views;

namespace Sillet.Mobile.UI
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
            MobilePlatform.ScreenshotTaken += OnScreenshot;
            MobilePlatform.NotificationsAllowed += OnNotificationsAllowed;
            MobilePlatform.OpenTargetRequested += OnTargetRequested;
            HookPull();
            ShowStartupLoader();
        }

        private void ShowStartupLoader()
        {
            var loader = this.FindControl<Border>("PageLoader");
            if (loader == null) return;
            var generation = ++_loaderGeneration;
            _splashActive = true;
            loader.Classes.Add("on");
            DispatcherTimer.RunOnce(() =>
            {
                _splashActive = false;
                if (generation == _loaderGeneration) loader.Classes.Remove("on");
            }, TimeSpan.FromMilliseconds(2500));
        }

        private MainViewModel Vm => DataContext as MainViewModel;

        private void OnTargetRequested(string target)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTarget());
        }

        private void ApplyTarget()
        {
            var vm = Vm;
            var target = MobilePlatform.PendingTarget;
            if (vm == null || string.IsNullOrEmpty(target)) return;
            MobilePlatform.PendingTarget = null;
            vm.OpenTarget(target);
        }
        private readonly Dictionary<object, Control> _views = new Dictionary<object, Control>();
        private MainViewModel _watched;
        private bool _warmed;

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_watched != null) _watched.PropertyChanged -= OnMainChanged;
            _watched = Vm;
            if (_watched != null) _watched.PropertyChanged += OnMainChanged;
            ApplyLite();
            ApplyTarget();
            ShowPage();
        }

        private bool _liteHooked;

        private void ApplyLite()
        {
            if (!_liteHooked)
            {
                _liteHooked = true;
                Performance.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(ApplyLite);
            }
            Classes.Set("lite", Performance.Lite);
        }

        private void OnMainChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage)) ShowPage();
            else if (e.PropertyName == nameof(MainViewModel.IsUnlocked) && _watched != null && !_watched.IsUnlocked) ClearViews();
        }

        private void ClearViews()
        {
            var host = this.FindControl<Panel>("PageHost");
            if (host != null) host.Children.Clear();
            _views.Clear();
            _scrollers.Clear();
            _current = null;
            _warmed = false;
        }

        private static Control CreateView(object page)
        {
            if (page is HomeViewModel) return new HomeView();
            if (page is MarketViewModel) return new MarketView();
            if (page is SendViewModel) return new SendView();
            if (page is ReceiveViewModel) return new ReceiveView();
            if (page is ActivityViewModel) return new ActivityView();
            if (page is SettingsViewModel) return new SettingsView();
            if (page is TradeViewModel) return new TradeView();
            if (page is DonateViewModel) return new DonateView();
            if (page is WalletConnectViewModel) return new WalletConnectView();
            return new SoonView();
        }

        private Control _current;

        private Control ViewFor(object page, Panel host)
        {
            Control view;
            if (_views.TryGetValue(page, out view)) return view;
            view = CreateView(page);
            view.DataContext = page;
            view.IsVisible = false;
            _views[page] = view;
            host.Children.Add(view);
            return view;
        }

        private int _loaderGeneration;
        private const int PageLoaderMs = 320;
        private bool _splashActive;
        private bool _applyQueued;

        private void ShowPage()
        {
            var host = this.FindControl<Panel>("PageHost");
            var vm = Vm;
            if (host == null) return;
            var page = vm == null ? null : vm.CurrentPage;
            if (page == null)
            {
                if (_current != null) _current.IsVisible = false;
                _current = null;
                return;
            }
            Control known;
            if (_views.TryGetValue(page, out known) && ReferenceEquals(_current, known)) return;

            var loader = this.FindControl<Border>("PageLoader");
            if (_current == null || loader == null || _splashActive)
            {
                ApplyPage();
                return;
            }
            _loaderGeneration++;
            if (!loader.Classes.Contains("on")) loader.Classes.Add("on");
            if (_applyQueued) return;
            _applyQueued = true;
            DispatcherTimer.RunOnce(() =>
            {
                _applyQueued = false;
                ApplyPage();
                var done = ++_loaderGeneration;
                DispatcherTimer.RunOnce(() =>
                {
                    if (done == _loaderGeneration) loader.Classes.Remove("on");
                }, TimeSpan.FromMilliseconds(140));
            }, TimeSpan.FromMilliseconds(PageLoaderMs));
        }

        private void ApplyPage()
        {
            var host = this.FindControl<Panel>("PageHost");
            var vm = Vm;
            if (host == null) return;
            var page = vm == null ? null : vm.CurrentPage;
            if (page == null)
            {
                if (_current != null) _current.IsVisible = false;
                _current = null;
                return;
            }
            var view = ViewFor(page, host);
            if (ReferenceEquals(_current, view)) return;
            view.Opacity = 1;
            view.IsHitTestVisible = true;
            view.IsVisible = true;
            if (_current != null) _current.IsVisible = false;
            _current = view;
            if (!_warmed)
            {
                _warmed = true;
                if (!Performance.Lite) DispatcherTimer.RunOnce(WarmUp, TimeSpan.FromMilliseconds(1800));
            }
        }

        private void WarmUp()
        {
            var vm = Vm;
            if (vm == null || !vm.IsUnlocked) return;
            WarmNext(new List<object> { vm.SettingsPage, vm.Markets, vm.Activity, vm.Receive, vm.Send, vm.Home, vm.Trade }, 0);
        }

        private void WarmNext(List<object> pages, int index)
        {
            var vm = Vm;
            var host = this.FindControl<Panel>("PageHost");
            if (vm == null || host == null || !vm.IsUnlocked || index >= pages.Count) return;
            var page = pages[index];
            if (page == null || _views.ContainsKey(page))
            {
                WarmNext(pages, index + 1);
                return;
            }
            var view = ViewFor(page, host);
            view.Opacity = 0;
            view.IsHitTestVisible = false;
            view.IsVisible = true;
            DispatcherTimer.RunOnce(() =>
            {
                if (!ReferenceEquals(_current, view))
                {
                    view.IsVisible = false;
                    view.Opacity = 1;
                    view.IsHitTestVisible = true;
                }
                WarmNext(pages, index + 1);
            }, TimeSpan.FromMilliseconds(260));
        }

        private Point _pullStart;
        private bool _pullTracking;
        private bool _pulling;
        private bool _pullBusy;
        private ScrollViewer _pullScroll;
        private double _pullDistance;
        private DispatcherTimer _spin;

        private void HookPull()
        {
            var host = this.FindControl<Panel>("PageHost");
            if (host == null) return;
            host.AddHandler(PointerPressedEvent, Pull_Pressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            host.AddHandler(PointerMovedEvent, Pull_Moved, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            host.AddHandler(PointerReleasedEvent, Pull_Released, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        }

        private readonly Dictionary<Control, List<ScrollViewer>> _scrollers = new Dictionary<Control, List<ScrollViewer>>();

        private ScrollViewer FindScroller(Control view)
        {
            List<ScrollViewer> list;
            if (!_scrollers.TryGetValue(view, out list) || list.Count == 0)
            {
                list = new List<ScrollViewer>();
                foreach (var d in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view))
                {
                    var found = d as ScrollViewer;
                    if (found != null) list.Add(found);
                }
                if (list.Count > 0) _scrollers[view] = list;
            }
            foreach (var sv in list)
                if (sv.IsVisible && sv.Bounds.Height > 100 && sv.IsEffectivelyVisible) return sv;
            return null;
        }

        private void Pull_Pressed(object sender, PointerPressedEventArgs e)
        {
            if (_pullBusy || _current == null || Vm == null || !Vm.IsUnlocked) return;

            if (_pullDistance > 0) ApplyPull(0, 0);
            _pullDistance = 0;

            var src = e.Source as Avalonia.Visual;
            while (src != null)
            {
                var b = src as Border;
                if (b != null && b.Name == "NewsCard") return;
                src = Avalonia.VisualTree.VisualExtensions.GetVisualParent(src);
            }

            _pullTracking = true;
            _pulling = false;
            _pullStart = e.GetPosition(this);
            _pullScroll = null;

            _pullScroll = FindScroller(_current);
        }

        private void Pull_Moved(object sender, PointerEventArgs e)
        {
            if (!_pullTracking || _pullBusy) return;

            var now = e.GetPosition(this);
            var dy = now.Y - _pullStart.Y;
            var dx = now.X - _pullStart.X;

            if (!_pulling)
            {
                if (dy < 14 || dy < Math.Abs(dx) * 1.4) return;
                if (_pullScroll != null && _pullScroll.Offset.Y > 1) return;

                _pulling = true;
                _pullStart = new Point(now.X, now.Y - 14);
                dy = 14;

                var host = this.FindControl<Panel>("PageHost");
                if (host != null) e.Pointer.Capture(host);
            }

            _pullDistance = Math.Clamp(dy * 0.45, 0, 110);

            if (_pullDistance <= 0)
            {
                ApplyPull(0, 0);
                e.Handled = true;
                return;
            }

            ApplyPull(_pullDistance, _pullDistance * 4.5);
            e.Handled = true;
        }

        private void Pull_Released(object sender, PointerReleasedEventArgs e)
        {
            if (_pulling) e.Pointer.Capture(null);
            FinishPull();
        }

        private void ApplyPull(double shift, double angle)
        {
            var host = this.FindControl<Panel>("PageHost");
            if (host != null)
            {
                var transform = host.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    host.RenderTransform = transform;
                }
                transform.Y = shift;
            }

            var icon = this.FindControl<Border>("PullIcon");
            if (icon != null)
            {
                icon.Margin = new Thickness(0, shift - 44, 0, 0);
                icon.Opacity = shift <= 0 ? 0 : Math.Min(1, shift / 50);

                var path = icon.Child as Avalonia.Controls.Shapes.Path;
                if (path != null)
                {
                    var spin = path.RenderTransform as RotateTransform;
                    if (spin == null)
                    {
                        spin = new RotateTransform();
                        path.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                        path.RenderTransform = spin;
                    }
                    spin.Angle = angle;
                }
            }
        }

        private async void FinishPull()
        {
            var wasPulling = _pulling;
            _pullTracking = false;
            _pulling = false;

            if (!wasPulling || _pullBusy) return;

            if (_pullDistance < 46)
            {
                ApplyPull(0, 0);
                return;
            }

            _pullBusy = true;
            ApplyPull(52, 0);

            var angle = 0.0;
            var spin = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
            spin.Tick += (s, a) =>
            {
                angle = (angle + 24) % 360;
                ApplyPull(52, angle);
            };
            spin.Start();
            _spin = spin;

            try
            {
                var vm = Vm;
                if (vm != null)
                {
                    var refresh = vm.RefreshAllAsync() ?? Task.CompletedTask;
                    await Task.WhenAny(
                        Task.WhenAll(refresh, Task.Delay(700)),
                        Task.Delay(5000));
                }
                else
                {
                    await Task.Delay(700);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                spin.Stop();
                if (ReferenceEquals(_spin, spin)) _spin = null;
                _pullBusy = false;
                _pullDistance = 0;
                ApplyPull(0, 0);
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var top = TopLevel.GetTopLevel(this);
            var insets = top == null ? null : top.InsetsManager;
            if (insets != null) insets.SafeAreaChanged += (s, args) => ApplySafeArea();
            SizeChanged += (s, args) => ApplySafeArea();
            ApplySafeArea();
            DispatcherTimer.RunOnce(ApplySafeArea, TimeSpan.FromMilliseconds(300));
            DispatcherTimer.RunOnce(ApplySafeArea, TimeSpan.FromMilliseconds(1200));
        }

        private void ApplySafeArea()
        {
            var top = TopLevel.GetTopLevel(this);
            var insets = top == null ? null : top.InsetsManager;
            var avalonia = insets == null ? default(Thickness) : insets.SafeAreaPadding;
            double left = avalonia.Left, up = avalonia.Top, right = avalonia.Right, down = avalonia.Bottom;
            var native = MobilePlatform.GetSafeArea == null ? null : MobilePlatform.GetSafeArea();
            if (native != null && native.Length == 4)
            {
                left = Math.Max(left, native[0]);
                up = Math.Max(up, native[1]);
                right = Math.Max(right, native[2]);
                down = Math.Max(down, native[3]);
            }
            var padding = new Thickness(left, up, right, down);
            if (Padding != padding) Padding = padding;
        }

        private void CurrencyScrim_Tapped(object sender, TappedEventArgs e)
        {
            var page = Vm == null ? null : Vm.SettingsPage;
            if (page != null) page.IsCurrencyPickerOpen = false;
        }

        private void PriceScrim_Tapped(object sender, TappedEventArgs e)
        {
            var page = Vm == null ? null : Vm.SettingsPage;
            if (page != null) page.IsPricePickerOpen = false;
        }

        private void Scrim_Tapped(object sender, TappedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            vm.IsProfilesOpen = false;
            vm.IsNotificationsOpen = false;
            var list = this.FindControl<ScrollViewer>("NotifList");
            if (list != null) list.MaxHeight = 380;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null || vm.Send == null) return;
            await QrFlow.Run(vm, vm.Send, false);
        }

        private void SheetAction_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null) return;
            vm.IsProfilesOpen = false;
            vm.IsNotificationsOpen = false;
        }

        private double? _grabStart;

        private void Grab_Pressed(object sender, PointerPressedEventArgs e)
        {
            var root = this.FindControl<Grid>("Root");
            _grabStart = e.GetPosition(root).Y;
            e.Pointer.Capture(sender as IInputElement);
        }

        private void Grab_Released(object sender, PointerReleasedEventArgs e)
        {
            var root = this.FindControl<Grid>("Root");
            var start = _grabStart;
            _grabStart = null;
            e.Pointer.Capture(null);
            var vm = Vm;
            if (start == null || vm == null || root == null) return;
            var delta = e.GetPosition(root).Y - start.Value;
            var tag = (sender as Control)?.Tag as string;
            var list = this.FindControl<ScrollViewer>("NotifList");
            var expanded = list != null && list.MaxHeight > 400;
            if (tag == "notifications" && list != null)
            {
                if (delta < -30 || (Math.Abs(delta) < 8 && !expanded)) list.MaxHeight = Math.Max(380, root.Bounds.Height - 200);
                else if (delta > 60 && !expanded) vm.IsNotificationsOpen = false;
                else if (delta > 30 || Math.Abs(delta) < 8) list.MaxHeight = 380;
                return;
            }
            if (delta > 60)
            {
                vm.IsProfilesOpen = false;
                vm.IsNotificationsOpen = false;
            }
        }

        private void OnNotificationsAllowed()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = Vm;
                if (vm == null || vm.Settings.PushNotifications) return;
                vm.Settings.PushNotifications = true;
                vm.Settings.NotificationsOn = true;
                vm.Settings.NotifyPayments = true;
                vm.Settings.NotifyNews = true;
                vm.Settings.Save();
                if (vm.SettingsPage != null) vm.SettingsPage.RaiseAll();
            });
        }

        private void OnScreenshot()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = Vm;
                if (vm == null || !vm.IsUnlocked || MobilePlatform.ShareImage == null || !vm.Settings.ScreenshotReminder) return;
                ShowPreview(Capture());
                ShowShot(true);
            });
        }

        private void ShowShot(bool open)
        {
            var scrim = this.FindControl<Border>("ShotScrim");
            var sheet = this.FindControl<Border>("ShotSheet");
            if (scrim == null || sheet == null) return;
            if (open)
            {
                if (!scrim.Classes.Contains("open")) scrim.Classes.Add("open");
                if (!sheet.Classes.Contains("open")) sheet.Classes.Add("open");
            }
            else
            {
                scrim.Classes.Remove("open");
                sheet.Classes.Remove("open");
            }
        }

        private byte[] _shotPng;

        private void ShowPreview(byte[] png)
        {
            _shotPng = png;
            var image = this.FindControl<Image>("ShotPreview");
            if (image == null) return;
            var old = image.Source as IDisposable;
            try
            {
                image.Source = png == null ? null : new Bitmap(new MemoryStream(png));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidOperationException)
            {
                image.Source = null;
            }
            if (old != null) old.Dispose();
            image.IsVisible = image.Source != null;
        }

        private void ShowSupport(bool open)
        {
            var scrim = this.FindControl<Border>("SupportScrim");
            var sheet = this.FindControl<Border>("SupportSheet");
            if (scrim == null || sheet == null) return;
            if (open)
            {
                if (!scrim.Classes.Contains("open")) scrim.Classes.Add("open");
                if (!sheet.Classes.Contains("open")) sheet.Classes.Add("open");
            }
            else
            {
                scrim.Classes.Remove("open");
                sheet.Classes.Remove("open");
            }
        }

        private void Support_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null)
            {
                vm.IsProfilesOpen = false;
                vm.IsNotificationsOpen = false;
            }
            ShowSupport(true);
        }

        private void SupportScrim_Tapped(object sender, TappedEventArgs e)
        {
            ShowSupport(false);
        }

        private void SupportTelegram_Click(object sender, RoutedEventArgs e)
        {
            ShowSupport(false);
            OpenLink("https://t.me/sillycs");
        }

        private void SupportDiscord_Click(object sender, RoutedEventArgs e)
        {
            ShowSupport(false);
            OpenLink("https://discord.gg/NBMWgX9Crq");
        }

        private void OpenLink(string url)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var ignored = top.Launcher.LaunchUriAsync(new Uri(url));
        }

        private void ShotSave_Click(object sender, RoutedEventArgs e)
        {
            ShowShot(false);
            var png = _shotPng ?? Capture();
            var save = MobilePlatform.SaveImage;
            if (png == null || save == null) return;
            save("sillet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", png);
        }

        private void ShotDismiss_Click(object sender, RoutedEventArgs e)
        {
            ShowShot(false);
        }

        private void ShotScrim_Tapped(object sender, TappedEventArgs e)
        {
            ShowShot(false);
        }

        private async void ShotShare_Click(object sender, RoutedEventArgs e)
        {
            ShowShot(false);
            await Task.Delay(220);
            var png = _shotPng ?? Capture();
            var share = MobilePlatform.ShareImage;
            if (png == null || share == null) return;
            share(Loc.T("Share"), "sillet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", png);
        }

        private byte[] Capture()
        {
            var root = this.FindControl<Grid>("Root");
            if (root == null || root.Bounds.Width <= 0 || root.Bounds.Height <= 0) return null;
            var top = TopLevel.GetTopLevel(this);
            var scale = top == null ? 1.0 : top.RenderScaling;
            var overlays = new List<Control>();
            foreach (var name in new[] { "ShotScrim", "ShotSheet", "SupportScrim", "SupportSheet" })
            {
                var overlay = this.FindControl<Control>(name);
                if (overlay != null && overlay.IsVisible) overlays.Add(overlay);
            }
            foreach (var overlay in overlays) overlay.IsVisible = false;
            try
            {
                var size = new PixelSize(Math.Max(1, (int)(root.Bounds.Width * scale)), Math.Max(1, (int)(root.Bounds.Height * scale)));
                using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
                using (var stream = new MemoryStream())
                {
                    bitmap.Render(root);
                    bitmap.Save(stream);
                    return stream.ToArray();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is ArgumentException)
            {
                return null;
            }
            finally
            {
                foreach (var overlay in overlays) overlay.IsVisible = true;
            }
        }
    }
}