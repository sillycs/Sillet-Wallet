using System;
using System.IO;
using Sillet.Net;
using Sillet.Storage;

namespace Sillet.App.Services
{
    public sealed class AppSettings
    {
        public AppSettings()
        {
            Currency = Currencies.Detect();
            Language = "en";
            Theme = "dark";
            AutoLockMinutes = 15;
            RefreshMinutes = 2;
            FavoriteCoins = "bitcoin,ethereum";
            StartPage = "home";
            DefaultFeeSpeed = "normal";
            ClipboardGuard = true;
            HomeCoin = "BTC";
            LockOnSystemLock = true;
            WarnNewAddress = true;
            WarnLookalike = true;
            SlowDownUnlock = true;
            LastUnlockUtc = "";
            NotificationsOn = true;
            SoundsOn = true;
            LastRunVersion = "";
            NotifyPayments = true;
            PushNotifications = true;
            RunInBackground = true;
            StartWithSystem = true;
            UpdateNotified = "";
            AutoUpdate = true;
            ScreenshotReminder = true;
            NotifyNews = true;
            NotifyBitcoin = true;
            BitcoinMovePercent = 5;
            AutoClearClipboard = true;
            BlockScreenCapture = true;
            LargeSendLevel = 2;
            LockVaultToDevice = true;
            HardenProcess = true;
            DefaultsVersion = CurrentDefaults;
        }

        public const int CurrentDefaults = 5;

        public string Currency { get; set; }
        public string Language { get; set; }
        public bool HideBalances { get; set; }
        public bool ShowEmptyAssets { get; set; }
        private bool _rotateReceive;
        public bool RotateReceive
        {
            get { return _rotateReceive; }
            set { _rotateReceive = value; Sillet.Chains.Utxo.UtxoChain.RotateReceive = value; }
        }
        public bool RotateBannerDismissed { get; set; }
        public string Theme { get; set; }
        public int AutoLockMinutes { get; set; }
        public int RefreshMinutes { get; set; }
        public bool HideSmallBalances { get; set; }
        public string FavoriteCoins { get; set; }
        public string StartPage { get; set; }
        public string DefaultFeeSpeed { get; set; }
        public bool RequirePasswordToSend { get; set; }
        public bool ClipboardGuard { get; set; }
        public bool AutoClearClipboard { get; set; }
        public string HomeCoin { get; set; }
        public bool LockOnMinimize { get; set; }
        public bool LockOnSystemLock { get; set; }
        public bool BlockScreenCapture { get; set; }
        public bool BlurWhenInactive { get; set; }
        public bool ContactsOnly { get; set; }
        public bool WarnNewAddress { get; set; }
        public bool WarnLookalike { get; set; }
        public int LargeSendLevel { get; set; }
        public bool SlowDownUnlock { get; set; }
        public int FailedUnlocks { get; set; }
        public string LastUnlockUtc { get; set; }
        public bool NotificationsOn { get; set; }
        public bool SoundsOn { get; set; }
        public string LastRunVersion { get; set; }
        public bool NotifyPayments { get; set; }
        public bool NotifyNews { get; set; }
        public bool NotifyBitcoin { get; set; }
        public int BitcoinMovePercent { get; set; }
        public bool PushNotifications { get; set; }
        public bool RunInBackground { get; set; }
        public bool PhraseViewed { get; set; }
        public bool BackupReminded { get; set; }
        public string UpdateNotified { get; set; }
        public bool AutoUpdate { get; set; }
        public string ProxyUrl { get; set; } = "";
        public string CustomEndpoints { get; set; } = "";

        public void ApplyNetwork()
        {
            Sillet.Net.Http.ProxyUrl = Sillet.Net.Http.ValidateProxy(ProxyUrl) == null ? ProxyUrl : null;
            Sillet.Net.Endpoints.Load(Sillet.Net.Endpoints.Validate(CustomEndpoints) == null ? CustomEndpoints : "");
        }
        public bool ScreenshotReminder { get; set; }
        public bool StartWithSystem { get; set; }
        public bool NavCollapsed { get; set; }
        public bool LockVaultToDevice { get; set; }
        public bool HardenProcess { get; set; }
        public int DefaultsVersion { get; set; }

        public static decimal LargeSendLimit(int level, decimal perUsd)
        {
            if (level <= 0) return 0;
            var usd = level == 1 ? 100m : 1000m;
            return Currencies.Nice(usd * (perUsd > 0 ? perUsd : 1m));
        }

        public static string DataFolder
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
                if (string.IsNullOrEmpty(root)) root = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(root, "Sillet");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DataFolder, "settings.json"); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return settings;
                var json = JsonValue.Parse(LocalData.ReadText(FilePath));
                JsonValue value;
                if (json.TryGet("Currency", out value) && value.IsString && Currencies.IsSupported(value.AsString())) settings.Currency = value.AsString().ToLowerInvariant();
                if (json.TryGet("Language", out value) && value.IsString) settings.Language = Loc.Normalize(value.AsString());
                if (json.TryGet("HideBalances", out value)) settings.HideBalances = value.IsTrue;
                if (json.TryGet("ShowEmptyAssets", out value)) settings.ShowEmptyAssets = value.IsTrue;
                if (json.TryGet("RotateReceive", out value)) settings.RotateReceive = value.IsTrue;
                if (json.TryGet("RotateBannerDismissed", out value)) settings.RotateBannerDismissed = value.IsTrue;
                if (json.TryGet("Theme", out value) && value.IsString && ThemeCatalog.Find(value.AsString()) != null) settings.Theme = value.AsString();
                if (json.TryGet("AutoLockMinutes", out value) && value.IsNumber) settings.AutoLockMinutes = Math.Max(0, Math.Min(240, value.AsInt()));
                if (json.TryGet("RefreshMinutes", out value) && value.IsNumber) settings.RefreshMinutes = Math.Max(1, Math.Min(60, value.AsInt()));
                if (json.TryGet("HideSmallBalances", out value)) settings.HideSmallBalances = value.IsTrue;
                if (json.TryGet("FavoriteCoins", out value) && value.IsString) settings.FavoriteCoins = value.AsString();
                if (json.TryGet("StartPage", out value) && value.IsString) settings.StartPage = value.AsString();
                if (json.TryGet("DefaultFeeSpeed", out value) && value.IsString) settings.DefaultFeeSpeed = value.AsString();
                if (json.TryGet("RequirePasswordToSend", out value)) settings.RequirePasswordToSend = value.IsTrue;
                if (json.TryGet("ClipboardGuard", out value) && value.Kind == JsonKind.Bool) settings.ClipboardGuard = value.IsTrue;
                if (json.TryGet("AutoClearClipboard", out value)) settings.AutoClearClipboard = value.IsTrue;
                if (json.TryGet("HomeCoin", out value) && value.IsString) settings.HomeCoin = value.AsString();
                if (json.TryGet("LockOnMinimize", out value)) settings.LockOnMinimize = value.IsTrue;
                if (json.TryGet("LockOnSystemLock", out value) && value.Kind == JsonKind.Bool) settings.LockOnSystemLock = value.IsTrue;
                if (json.TryGet("BlockScreenCapture", out value)) settings.BlockScreenCapture = value.IsTrue;
                if (json.TryGet("BlurWhenInactive", out value)) settings.BlurWhenInactive = value.IsTrue;
                if (json.TryGet("ContactsOnly", out value)) settings.ContactsOnly = value.IsTrue;
                if (json.TryGet("WarnNewAddress", out value) && value.Kind == JsonKind.Bool) settings.WarnNewAddress = value.IsTrue;
                if (json.TryGet("WarnLookalike", out value) && value.Kind == JsonKind.Bool) settings.WarnLookalike = value.IsTrue;
                if (json.TryGet("LargeSendLevel", out value) && value.IsNumber) settings.LargeSendLevel = Math.Max(0, Math.Min(2, value.AsInt()));
                if (json.TryGet("SlowDownUnlock", out value) && value.Kind == JsonKind.Bool) settings.SlowDownUnlock = value.IsTrue;
                if (json.TryGet("FailedUnlocks", out value) && value.IsNumber) settings.FailedUnlocks = Math.Max(0, Math.Min(100000, value.AsInt()));
                if (json.TryGet("LastUnlockUtc", out value) && value.IsString) settings.LastUnlockUtc = value.AsString();
                if (json.TryGet("NotificationsOn", out value) && value.Kind == JsonKind.Bool) settings.NotificationsOn = value.IsTrue;
                if (json.TryGet("SoundsOn", out value) && value.Kind == JsonKind.Bool) settings.SoundsOn = value.IsTrue;
                if (json.TryGet("LastRunVersion", out value) && value.IsString) settings.LastRunVersion = value.AsString();
                if (json.TryGet("NotifyPayments", out value) && value.Kind == JsonKind.Bool) settings.NotifyPayments = value.IsTrue;
                if (json.TryGet("NotifyNews", out value) && value.Kind == JsonKind.Bool) settings.NotifyNews = value.IsTrue;
                if (json.TryGet("NotifyBitcoin", out value)) settings.NotifyBitcoin = value.IsTrue;
                if (json.TryGet("BitcoinMovePercent", out value) && value.IsNumber) settings.BitcoinMovePercent = Math.Max(1, Math.Min(50, value.AsInt()));
                if (json.TryGet("PushNotifications", out value)) settings.PushNotifications = value.IsTrue;
                if (json.TryGet("PhraseViewed", out value) && value.Kind == JsonKind.Bool) settings.PhraseViewed = value.IsTrue;
                if (json.TryGet("BackupReminded", out value) && value.Kind == JsonKind.Bool) settings.BackupReminded = value.IsTrue;
                if (json.TryGet("UpdateNotified", out value) && value.IsString) settings.UpdateNotified = value.AsString();
                if (json.TryGet("ScreenshotReminder", out value) && value.Kind == JsonKind.Bool) settings.ScreenshotReminder = value.IsTrue;
                if (json.TryGet("AutoUpdate", out value) && value.Kind == JsonKind.Bool) settings.AutoUpdate = value.IsTrue;
                if (json.TryGet("ProxyUrl", out value) && value.IsString) settings.ProxyUrl = value.AsString();
                if (json.TryGet("CustomEndpoints", out value) && value.IsString) settings.CustomEndpoints = value.AsString();
                if (json.TryGet("RunInBackground", out value) && value.Kind == JsonKind.Bool) settings.RunInBackground = value.IsTrue;
                if (json.TryGet("StartWithSystem", out value) && value.Kind == JsonKind.Bool) settings.StartWithSystem = value.IsTrue;
                if (json.TryGet("NavCollapsed", out value)) settings.NavCollapsed = value.IsTrue;
                if (json.TryGet("LockVaultToDevice", out value) && value.Kind == JsonKind.Bool) settings.LockVaultToDevice = value.IsTrue;
                if (json.TryGet("HardenProcess", out value) && value.Kind == JsonKind.Bool) settings.HardenProcess = value.IsTrue;
                settings.DefaultsVersion = json.TryGet("DefaultsVersion", out value) && value.IsNumber ? value.AsInt() : 1;
                if (settings.DefaultsVersion < 2)
                {
                    settings.AutoClearClipboard = true;
                    settings.BlockScreenCapture = true;
                    settings.LockOnSystemLock = true;
                    settings.SlowDownUnlock = true;
                    settings.ClipboardGuard = true;
                    settings.WarnNewAddress = true;
                    settings.WarnLookalike = true;
                    if (settings.LargeSendLevel == 0) settings.LargeSendLevel = 2;
                    settings.LockVaultToDevice = true;
                    settings.HardenProcess = true;
                }
                if (settings.DefaultsVersion < 3 && settings.Currency == "clp") settings.Currency = Currencies.Detect();
                if (settings.DefaultsVersion < 4)
                {
                    settings.PushNotifications = true;
                    settings.NotificationsOn = true;
                    settings.NotifyPayments = true;
                    settings.RunInBackground = true;
                    settings.StartWithSystem = true;
                }
                if (settings.DefaultsVersion < 5)
                {
                    // Bitcoin move alerts used to be off, so nobody ever saw them.
                    settings.NotifyBitcoin = true;
                    settings.NotifyNews = true;
                    if (settings.BitcoinMovePercent <= 0) settings.BitcoinMovePercent = 5;
                }
                if (settings.DefaultsVersion < CurrentDefaults)
                {
                    settings.DefaultsVersion = CurrentDefaults;
                    settings.Save();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException || e is OverflowException)
            {
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                var json = Json.Serialize(new { Currency = Currency, Language = Language ?? "en", HideBalances = HideBalances, ShowEmptyAssets = ShowEmptyAssets, RotateReceive = RotateReceive, RotateBannerDismissed = RotateBannerDismissed, Theme = Theme, AutoLockMinutes = AutoLockMinutes, RefreshMinutes = RefreshMinutes, HideSmallBalances = HideSmallBalances, FavoriteCoins = FavoriteCoins,
                    StartPage = StartPage, DefaultFeeSpeed = DefaultFeeSpeed, RequirePasswordToSend = RequirePasswordToSend,
                    ClipboardGuard = ClipboardGuard, AutoClearClipboard = AutoClearClipboard, HomeCoin = HomeCoin,
                    LockOnMinimize = LockOnMinimize, LockOnSystemLock = LockOnSystemLock, BlockScreenCapture = BlockScreenCapture, BlurWhenInactive = BlurWhenInactive,
                    ContactsOnly = ContactsOnly, WarnNewAddress = WarnNewAddress, WarnLookalike = WarnLookalike, LargeSendLevel = LargeSendLevel, SlowDownUnlock = SlowDownUnlock,
                    FailedUnlocks = FailedUnlocks, LastUnlockUtc = LastUnlockUtc ?? "",
                    SoundsOn = SoundsOn, LastRunVersion = LastRunVersion ?? "",
                    NotificationsOn = NotificationsOn, NotifyPayments = NotifyPayments, NotifyNews = NotifyNews, NotifyBitcoin = NotifyBitcoin,
                    BitcoinMovePercent = BitcoinMovePercent, PushNotifications = PushNotifications, PhraseViewed = PhraseViewed, BackupReminded = BackupReminded, UpdateNotified = UpdateNotified ?? "", AutoUpdate = AutoUpdate, ProxyUrl = ProxyUrl ?? "", CustomEndpoints = CustomEndpoints ?? "", ScreenshotReminder = ScreenshotReminder, RunInBackground = RunInBackground, StartWithSystem = StartWithSystem, NavCollapsed = NavCollapsed,
                    LockVaultToDevice = LockVaultToDevice, HardenProcess = HardenProcess, DefaultsVersion = DefaultsVersion });
                LocalData.WriteText(FilePath, json);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
