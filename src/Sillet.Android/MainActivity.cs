// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using Sillet.App.Services;
using Sillet.Mobile;

namespace Sillet.Droid
{
    [Activity(Label = "Sillet", Theme = "@style/SilletTheme", Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round", MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    // Links that open Sillet: sillet://, wc: and https://raton.fun/wallet/wc (the last one needs the assetlinks.json file on raton.fun to open without asking)
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "sillet")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "wc")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable }, DataScheme = "https", DataHost = "raton.fun", DataPathPrefix = "/wallet/wc", AutoVerify = true)]
    public class MainActivity : AvaloniaMainActivity<Sillet.Mobile.App>
    {
        private CaptureCallback _capture;
        private bool _active;
        private TaskCompletionSource<string> _scanTask;

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder).WithInterFont()
                .With(new AndroidPlatformOptions { RenderingMode = new[] { AndroidRenderingMode.Egl, AndroidRenderingMode.Software } });
        }

        private void RestartApp()
        {
            try
            {
                var launch = PackageManager.GetLaunchIntentForPackage(PackageName);
                launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
                var pending = PendingIntent.GetActivity(this, 4242, launch, PendingIntentFlags.Immutable | PendingIntentFlags.CancelCurrent);
                var alarms = (AlarmManager)GetSystemService(AlarmService);
                alarms.Set(AlarmType.RtcWakeup, Java.Lang.JavaSystem.CurrentTimeMillis() + 400, pending);
                FinishAffinity();
                Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            ReadTarget(intent);
        }

        private static void ReadTarget(Intent intent)
        {
            try
            {
                // a link that opened Sillet
                var link = intent == null ? null : intent.DataString;
                if (!string.IsNullOrEmpty(link) && Sillet.WalletConnect.WalletConnectLinks.Extract(link) != null)
                {
                    intent.SetData(null);
                    MobilePlatform.RequestTarget(link);
                    return;
                }
                var target = intent == null ? null : intent.GetStringExtra(AlertNotifier.TargetExtra);
                if (!string.IsNullOrEmpty(target))
                {
                    intent.RemoveExtra(AlertNotifier.TargetExtra);
                    MobilePlatform.RequestTarget(target);
                }
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            UpdateChannel.Apply(this);
            Performance.Restart = RestartApp;
            AppSettings.DefaultLite = () =>
            {
                try
                {
                    var manager = (ActivityManager)GetSystemService(ActivityService);
                    return manager.IsLowRamDevice || manager.MemoryClass <= 192 || Java.Lang.Runtime.GetRuntime().AvailableProcessors() <= 4 || (int)Build.VERSION.SdkInt <= 28;
                }
                catch (Java.Lang.Exception)
                {
                    return false;
                }
            };
            ReadTarget(Intent);
            MobilePlatform.SetSecure = on => RunOnUiThread(() =>
            {
                if (on) Window.AddFlags(WindowManagerFlags.Secure);
                else Window.ClearFlags(WindowManagerFlags.Secure);
            });
            MobilePlatform.ShareText = (title, text) => RunOnUiThread(() => ShareText(title, text));
            MobilePlatform.ShareImage = (title, fileName, png) => RunOnUiThread(() => ShareImage(title, fileName, png));
            MobilePlatform.SaveImage = (fileName, png) => RunOnUiThread(() => SaveImage(fileName, png));
            MobilePlatform.OpenBrowser = url => RunOnUiThread(() => StartActivity(new Intent(this, typeof(BrowserActivity)).PutExtra("url", url)));
            MobilePlatform.ConnectLedger = ConnectLedgerAsync;
            MobilePlatform.ShareFile = (title, fileName, data, mime) => RunOnUiThread(() => ShareFile(title, fileName, data, mime));
            MobilePlatform.IsActive = () => _active;
            MobilePlatform.GetSafeArea = ReadSafeArea;
            var biometrics = new Biometrics(this);
            MobilePlatform.PasskeyUnavailable = biometrics.Unavailable;
            MobilePlatform.PasskeyCreate = biometrics.CreateAsync;
            MobilePlatform.PasskeyDecrypt = biometrics.DecryptAsync;
            MobilePlatform.PasskeyDelete = biometrics.DeleteKey;
            MobilePlatform.CopyText = text => RunOnUiThread(() => CopyToClipboard(text));
            MobilePlatform.ClearClipboardIf = text => RunOnUiThread(() => ClearClipboard(text));
            MobilePlatform.ScanQr = pickFile =>
            {
                var source = new TaskCompletionSource<string>();
                RunOnUiThread(() =>
                {
                    try
                    {
                        if (_scanTask != null) _scanTask.TrySetResult(null);
                        _scanTask = source;
                        var intent = new Intent(this, typeof(ScanActivity));
                        intent.PutExtra("pick", pickFile);
                        StartActivityForResult(intent, 21);
                    }
                    catch (Java.Lang.Exception)
                    {
                        source.TrySetResult(null);
                    }
                });
                return source.Task;
            };
            var pool = new Android.Media.SoundPool.Builder()
                .SetMaxStreams(4)
                .SetAudioAttributes(new Android.Media.AudioAttributes.Builder().SetUsage(Android.Media.AudioUsageKind.AssistanceSonification).SetContentType(Android.Media.AudioContentType.Sonification).Build())
                .Build();
            var poolIds = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var poolReady = new System.Collections.Concurrent.ConcurrentDictionary<int, bool>();
            pool.LoadComplete += (s, e) => { if (e.Status == 0) poolReady[e.SampleId] = true; };
            MobilePlatform.PreloadSound = (name, bytes) =>
            {
                try
                {
                    var path = System.IO.Path.Combine(CacheDir.AbsolutePath, "snd-" + name + ".mp3");
                    if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, bytes);
                    poolIds.GetOrAdd(name, n => pool.Load(path, 1));
                }
                catch (System.Exception)
                {
                }
            };
            MobilePlatform.PlaySound = (name, bytes) => RunOnUiThread(() =>
            {
                try
                {
                    int id;
                    if (poolIds.TryGetValue(name, out id) && poolReady.ContainsKey(id))
                    {
                        pool.Play(id, 1f, 1f, 1, 0, 1f);
                        return;
                    }
                    var path = System.IO.Path.Combine(CacheDir.AbsolutePath, "snd-" + name + ".mp3");
                    if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, bytes);
                    var player = new Android.Media.MediaPlayer();
                    player.SetDataSource(path);
                    player.Completion += (s, e) => player.Release();
                    player.Error += (s, e) => { player.Release(); };
                    player.Prepared += (s, e) => player.Start();
                    player.PrepareAsync();
                }
                catch (System.Exception)
                {
                }
            });
            MobilePlatform.Notify = (title, message, kind) => RunOnUiThread(() => ShowNotification(title, message, kind));
            MobilePlatform.SetBackground = on => RunOnUiThread(() => ApplyBackground(on));
            base.OnCreate(savedInstanceState);
            MobilePlatform.SetSecure(AppSettings.Load().BlockScreenCapture);
            CreateChannel();
            AskNotificationPermission();
            if (AppSettings.Load().RunInBackground) WatcherService.Begin(this);
        }

        private TaskCompletionSource<bool> _bluetoothPermission;

        private async Task<Sillet.Hardware.ILedgerTransport> ConnectLedgerAsync()
        {
            var usb = await LedgerUsb.OpenAsync(this);
            if (usb != null) return usb;
            if (!await EnsureBluetoothPermissionAsync()) return null;
            return await LedgerBle.OpenAsync(this);
        }

        private Task<bool> EnsureBluetoothPermissionAsync()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return Task.FromResult(true);
            if (CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) == Permission.Granted && CheckSelfPermission(Android.Manifest.Permission.BluetoothScan) == Permission.Granted)
                return Task.FromResult(true);
            _bluetoothPermission = new TaskCompletionSource<bool>();
            RunOnUiThread(() => RequestPermissions(new[] { Android.Manifest.Permission.BluetoothConnect, Android.Manifest.Permission.BluetoothScan }, 31));
            return _bluetoothPermission.Task;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == 31 && _bluetoothPermission != null)
                _bluetoothPermission.TrySetResult(grantResults != null && grantResults.Length > 0 && grantResults.All(g => g == Permission.Granted));
            if (requestCode == 7 && grantResults != null && grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                MobilePlatform.RaiseNotificationsAllowed();
                if (AppSettings.Load().RunInBackground) WatcherService.Begin(this);
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != 21) return;
            var source = _scanTask;
            _scanTask = null;
            if (source != null) source.TrySetResult(resultCode == Result.Ok && data != null ? data.GetStringExtra("text") : null);
        }

        private const float TargetRefreshRate = 60f;

        private void AskForSteadyDisplayMode()
        {
            try
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(23)) return;
                var display = WindowManager.DefaultDisplay;
                var current = display == null ? null : display.GetMode();
                if (current == null) return;
                Android.Views.Display.Mode best = null;
                foreach (var mode in display.GetSupportedModes())
                {
                    if (mode.PhysicalWidth != current.PhysicalWidth || mode.PhysicalHeight != current.PhysicalHeight) continue;
                    if (mode.RefreshRate > TargetRefreshRate + 1f) continue;
                    if (best == null || mode.RefreshRate > best.RefreshRate) best = mode;
                }
                if (best == null || best.ModeId == current.ModeId) return;
                var attributes = Window.Attributes;
                attributes.PreferredDisplayModeId = best.ModeId;
                Window.Attributes = attributes;
            }
            catch (Exception e) when (e is Java.Lang.Exception || e is InvalidOperationException || e is NullReferenceException)
            {
            }
        }

        protected override void OnResume()
        {
            base.OnResume();
            AskForSteadyDisplayMode();
            _active = true;
        }

        protected override void OnPause()
        {
            _active = false;
            base.OnPause();
        }

        private double[] ReadSafeArea()
        {
            try
            {
                var insets = Window == null || Window.DecorView == null ? null : Window.DecorView.RootWindowInsets;
                if (insets == null) return null;
                var density = Resources.DisplayMetrics.Density;
                if (density <= 0) density = 1;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                    return new double[] { bars.Left / density, bars.Top / density, bars.Right / density, bars.Bottom / density };
                }
                return new double[]
                {
                    insets.SystemWindowInsetLeft / density, insets.SystemWindowInsetTop / density,
                    insets.SystemWindowInsetRight / density, insets.SystemWindowInsetBottom / density
                };
            }
            catch (Java.Lang.Exception)
            {
                return null;
            }
        }

        private void CreateChannel()
        {
            AlertNotifier.EnsureChannels(this);
        }

        private void AskNotificationPermission()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
            if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted) return;
            var prefs = GetSharedPreferences("sillet", FileCreationMode.Private);
            if (prefs == null || prefs.GetBoolean("asked_notifications", false)) return;
            prefs.Edit().PutBoolean("asked_notifications", true).Apply();
            RequestPermissions(new[] { Android.Manifest.Permission.PostNotifications }, 7);
        }

        private void ShowNotification(string title, string message, string kind)
        {
            AlertNotifier.Show(this, title, message, kind);
        }

        private void ApplyBackground(bool on)
        {
            if (on) WatcherService.Begin(this);
            else WatcherService.End(this);
        }

        protected override void OnStart()
        {
            base.OnStart();
            if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;
            if (_capture == null) _capture = new CaptureCallback();
            RegisterScreenCaptureCallback(MainExecutor, _capture);
        }

        protected override void OnStop()
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34) && _capture != null) UnregisterScreenCaptureCallback(_capture);
            base.OnStop();
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                var manager = GetSystemService(ClipboardService) as ClipboardManager;
                if (manager == null) return;
                var clip = ClipData.NewPlainText("Sillet", text);
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    var extras = new PersistableBundle();
                    extras.PutBoolean("android.content.extra.IS_SENSITIVE", true);
                    clip.Description.Extras = extras;
                }
                manager.PrimaryClip = clip;
            }
            catch (Java.Lang.Exception e)
            {
                Toast.MakeText(this, e.Message, ToastLength.Short).Show();
            }
        }

        private void ClearClipboard(string text)
        {
            try
            {
                var manager = GetSystemService(ClipboardService) as ClipboardManager;
                if (manager == null || manager.PrimaryClip == null || manager.PrimaryClip.ItemCount == 0) return;
                var current = manager.PrimaryClip.GetItemAt(0).Text;
                if (current != text) return;
                if (OperatingSystem.IsAndroidVersionAtLeast(28)) manager.ClearPrimaryClip();
                else manager.PrimaryClip = ClipData.NewPlainText("", "");
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        private void ShareText(string title, string text)
        {
            try
            {
                var intent = new Intent(Intent.ActionSend);
                intent.SetType("text/plain");
                intent.PutExtra(Intent.ExtraText, text);
                StartActivity(Intent.CreateChooser(intent, title));
            }
            catch (Java.Lang.Exception e)
            {
                Toast.MakeText(this, e.Message, ToastLength.Long).Show();
            }
        }

        private void SaveImage(string fileName, byte[] png)
        {
            try
            {
                var values = new ContentValues();
                values.Put("_display_name", fileName);
                values.Put("mime_type", "image/png");
                if (OperatingSystem.IsAndroidVersionAtLeast(29)) values.Put("relative_path", "Pictures/Sillet");
                var uri = ContentResolver.Insert(Android.Provider.MediaStore.Images.Media.ExternalContentUri, values);
                if (uri == null) throw new System.IO.IOException("no gallery");
                using (var stream = ContentResolver.OpenOutputStream(uri))
                {
                    stream.Write(png, 0, png.Length);
                }
                Toast.MakeText(this, Loc.T("Saved to your gallery"), ToastLength.Short).Show();
            }
            catch (Exception e) when (e is Java.Lang.Exception || e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Toast.MakeText(this, e.Message, ToastLength.Long).Show();
            }
        }

        private void ShareImage(string title, string fileName, byte[] png)
        {
            ShareFile(title, fileName, png, "image/png");
        }

        private void ShareFile(string title, string fileName, byte[] data, string mime)
        {
            try
            {
                var dir = new Java.IO.File(CacheDir, "shared");
                dir.Mkdirs();
                foreach (var old in dir.ListFiles() ?? new Java.IO.File[0]) old.Delete();
                var file = new Java.IO.File(dir, fileName);
                System.IO.File.WriteAllBytes(file.AbsolutePath, data);
                var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".fileprovider", file);
                var intent = new Intent(Intent.ActionSend);
                intent.SetType(mime);
                intent.PutExtra(Intent.ExtraStream, uri);
                intent.ClipData = ClipData.NewRawUri("", uri);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                StartActivity(Intent.CreateChooser(intent, title));
            }
            catch (Exception e) when (e is Java.Lang.Exception || e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Toast.MakeText(this, e.Message, ToastLength.Long).Show();
            }
        }

        private sealed class CaptureCallback : Java.Lang.Object, Activity.IScreenCaptureCallback
        {
            public void OnScreenCaptured()
            {
                MobilePlatform.RaiseScreenshot();
            }
        }
    }
}
