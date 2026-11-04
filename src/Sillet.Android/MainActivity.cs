using System;
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
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    public class MainActivity : AvaloniaMainActivity<Sillet.Mobile.App>
    {
        private CaptureCallback _capture;
        private bool _active;
        private int _nextNotification = 100;
        private TaskCompletionSource<string> _scanTask;

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder).WithInterFont();
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
            ReadTarget(Intent);
            MobilePlatform.SetSecure = on => RunOnUiThread(() =>
            {
                if (on) Window.AddFlags(WindowManagerFlags.Secure);
                else Window.ClearFlags(WindowManagerFlags.Secure);
            });
            MobilePlatform.ShareText = (title, text) => RunOnUiThread(() => ShareText(title, text));
            MobilePlatform.ShareImage = (title, fileName, png) => RunOnUiThread(() => ShareImage(title, fileName, png));
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
            MobilePlatform.PlaySound = (name, bytes) => RunOnUiThread(() =>
            {
                try
                {
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

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
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

        /// <summary>Many phones keep apps at 60 Hz even when the screen can do 90 or 120, which makes scrolling feel slow. Ask for the fastest mode at the same resolution.</summary>
        // 60 Hz on purpose: the interface cannot always draw a frame in the 8 ms that 120 Hz leaves, and a steady 60 feels smoother than 120 that stutters.
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
