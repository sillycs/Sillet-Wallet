// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Sillet.App.Services;

namespace Sillet.Droid
{
    internal static class AlertNotifier
    {
        public const string AlertsChannel = "sillet_alerts";
        public const string WatchChannel = "sillet_watch";
        private static int _next = 100;

        public const string PaymentsChannel = "sillet_payments";
        public const string MarketChannel = "sillet_market";
        public const string SecurityChannel = "sillet_security";

        private static string ChannelFor(string kind)
        {
            switch (kind)
            {
                case "payment": return PaymentsChannel;
                case "security": case "update": return SecurityChannel;
                case "price": case "price_up": case "price_down": case "alert": case "news": return MarketChannel;
                default: return AlertsChannel;
            }
        }

        private static string ColorFor(string kind)
        {
            switch (kind)
            {
                case "payment": case "price_up": return "#3DD68C";
                case "price_down": return "#F2555A";
                case "security": case "update": return "#E2B464";
                case "news": return "#7AA7FF";
                default: return "#B9B9C2";
            }
        }

        private static string GlyphFor(string kind)
        {
            switch (kind)
            {
                case "payment": return "↓";
                case "price_up": return "↗";
                case "price_down": return "↘";
                case "price": case "alert": return "◆";
                case "security": return "!";
                case "update": return "↑";
                case "news": return "i";
                default: return "•";
            }
        }

        public static void EnsureChannels(Context context)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            if (manager == null) return;
            var payments = new NotificationChannel(PaymentsChannel, Loc.T("Payments"), NotificationImportance.High)
            {
                Description = Loc.T("Money that arrives in your wallets")
            };
            payments.EnableVibration(true);
            payments.SetVibrationPattern(new long[] { 0, 180, 90, 180 });
            payments.EnableLights(true);
            payments.LightColor = Android.Graphics.Color.ParseColor("#3DD68C");
            manager.CreateNotificationChannel(payments);
            manager.CreateNotificationChannel(new NotificationChannel(MarketChannel, Loc.T("News and prices"), NotificationImportance.Default)
            {
                Description = Loc.T("News, price alerts and Bitcoin moves")
            });
            manager.CreateNotificationChannel(new NotificationChannel(SecurityChannel, Loc.T("Security and updates"), NotificationImportance.High)
            {
                Description = Loc.T("Backup reminders and new versions")
            });
            manager.CreateNotificationChannel(new NotificationChannel(AlertsChannel, Loc.T("Sillet alerts"), NotificationImportance.Default)
            {
                Description = Loc.T("Payments, news and price moves")
            });
            var watch = new NotificationChannel(WatchChannel, Loc.T("Payment watcher"), NotificationImportance.Min)
            {
                Description = Loc.T("Keeps Sillet watching for incoming payments")
            };
            watch.SetShowBadge(false);
            manager.CreateNotificationChannel(watch);
        }

        private static Android.Graphics.Bitmap LargeIcon(string kind)
        {
            const int size = 128;
            var bitmap = Android.Graphics.Bitmap.CreateBitmap(size, size, Android.Graphics.Bitmap.Config.Argb8888);
            var canvas = new Android.Graphics.Canvas(bitmap);
            var paint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
            var color = Android.Graphics.Color.ParseColor(ColorFor(kind));
            paint.Color = Android.Graphics.Color.Argb(60, color.R, color.G, color.B);
            canvas.DrawCircle(size / 2f, size / 2f, size / 2f, paint);
            paint.Color = color;
            paint.TextSize = 70;
            paint.TextAlign = Android.Graphics.Paint.Align.Center;
            paint.FakeBoldText = true;
            canvas.DrawText(GlyphFor(kind), size / 2f, size / 2f + 24, paint);
            return bitmap;
        }

        public const string TargetExtra = "sillet_target";

        public static string TargetFor(string kind)
        {
            switch (kind)
            {
                case "payment": return "activity";
                case "security": return "settings";
                case "update": return "settings";
                case "price":
                case "price_up":
                case "price_down": return "market";
                default: return "home";
            }
        }

        private static PendingIntent OpenApp(Context context, string target = null)
        {
            var launch = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
            if (launch != null)
            {
                launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
                if (target != null) launch.PutExtra(TargetExtra, target);
            }
            var code = target == null ? 0 : 1 + Math.Abs(target.GetHashCode() % 1000);
            return PendingIntent.GetActivity(context, code, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        }

        public static void Show(Context context, string title, string message, string kind = "info")
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(33) && context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted) return;
                var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
                if (manager == null) return;
                EnsureChannels(context);
                kind = kind ?? "info";
                var pending = OpenApp(context, TargetFor(kind));
                var accent = Android.Graphics.Color.ParseColor(ColorFor(kind));
                var channel = ChannelFor(kind);
                var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(context, channel) : new Notification.Builder(context);
                builder.SetSmallIcon(Resource.Drawable.notify_icon)
                       .SetLargeIcon(LargeIcon(kind))
                       .SetColor(accent)
                       .SetContentTitle(title)
                       .SetContentText(message ?? "")
                       .SetStyle(new Notification.BigTextStyle().BigText(message ?? ""))
                       .SetCategory(kind == "payment" ? Notification.CategoryEvent : kind == "news" ? Notification.CategoryRecommendation : Notification.CategoryStatus)
                       .SetGroup("sillet_" + channel)
                       .SetShowWhen(true)
                       .SetAutoCancel(true)
                       .SetContentIntent(pending);
                if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    builder.SetPriority((int)(kind == "payment" || kind == "security" ? NotificationPriority.High : NotificationPriority.Default));
                    if (kind == "payment") builder.SetVibrate(new long[] { 0, 180, 90, 180 });
                }
                builder.AddAction(Resource.Drawable.notify_icon, kind == "payment" ? Loc.T("See in Activity") : Loc.T("Open Sillet"), pending);
                builder.SetVisibility(NotificationVisibility.Private);
                var hidden = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(context, channel) : new Notification.Builder(context);
                hidden.SetSmallIcon(Resource.Drawable.notify_icon).SetColor(accent).SetContentTitle(title);
                builder.SetPublicVersion(hidden.Build());
                manager.Notify(_next++, builder.Build());
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        public static Notification Watching(Context context)
        {
            EnsureChannels(context);
            var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(context, WatchChannel) : new Notification.Builder(context);
            builder.SetSmallIcon(Resource.Drawable.notify_icon)
                   .SetContentTitle("Sillet")
                   .SetContentText(Loc.T("Watching for incoming payments"))
                   .SetOngoing(true)
                   .SetContentIntent(OpenApp(context));
            return builder.Build();
        }
    }
}
