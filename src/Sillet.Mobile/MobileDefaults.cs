// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using Sillet.App.Services;

namespace Sillet.Mobile
{
    public static class MobileDefaults
    {
        public static void Apply()
        {
            ThemeCatalog.Mobile = OperatingSystem.IsAndroid();
            if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS()) return;
            try
            {
                if (File.Exists(Path.Combine(AppSettings.DataFolder, "settings.json"))) return;
                var s = AppSettings.Load();
                s.AutoLockMinutes = 0;
                s.ClipboardGuard = false;
                s.AutoClearClipboard = false;
                s.LockOnSystemLock = false;
                s.LockOnMinimize = false;
                s.WarnNewAddress = false;
                s.SlowDownUnlock = false;
                s.BlockScreenCapture = false;
                s.BlurWhenInactive = false;
                s.LargeSendLevel = 0;
                s.LockVaultToDevice = false;
                s.HardenProcess = false;
                s.RequirePasswordToSend = false;
                s.ContactsOnly = false;
                s.HideBalances = false;
                s.HideSmallBalances = false;
                s.NotificationsOn = true;
                s.NotifyPayments = true;
                s.NotifyNews = false;
                s.NotifyBitcoin = false;
                s.PushNotifications = true;
                s.RunInBackground = true;
                s.StartWithSystem = true;
                s.Save();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
