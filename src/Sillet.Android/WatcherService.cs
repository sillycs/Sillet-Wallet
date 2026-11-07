// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Sillet.App.Services;
using Sillet.Mobile;

namespace Sillet.Droid
{
    [Register("fun.raton.sillet.WatcherService")]
    public class WatcherService : Service
    {
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            try
            {
                var notification = AlertNotifier.Watching(this);
                if (OperatingSystem.IsAndroidVersionAtLeast(34)) StartForeground(1, notification, ForegroundService.TypeSpecialUse);
                else StartForeground(1, notification);
            }
            catch (Java.Lang.Exception)
            {
                StopSelf();
                return StartCommandResult.NotSticky;
            }
            var context = ApplicationContext;
            UpdateChannel.Apply(context);
            var watcher = PaymentWatcher.Instance;
            watcher.IsAppActive = () =>
            {
                var check = MobilePlatform.IsActive;
                return check != null && check();
            };
            watcher.Notifier = (title, message, kind) => AlertNotifier.Show(context, title, message, kind);
            watcher.Start();
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            PaymentWatcher.Instance.Stop();
            base.OnDestroy();
        }

        public static void Begin(Context context)
        {
            try
            {
                var intent = new Intent(context, typeof(WatcherService));
                if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
                else context.StartService(intent);
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        public static void End(Context context)
        {
            try
            {
                context.StopService(new Intent(context, typeof(WatcherService)));
            }
            catch (Java.Lang.Exception)
            {
            }
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = true, Name = "fun.raton.sillet.BootReceiver")]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
    public class BootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            try
            {
                var settings = AppSettings.Load();
                if (settings.RunInBackground && settings.StartWithSystem) WatcherService.Begin(context);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
