using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.Core;
using Sillet.Storage;

namespace Sillet.App.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private object _authPage;
        private object _currentPage;
        private NavItem _selectedNav;
        private bool _isUnlocked;
        private string _toastMessage;
        private bool _toastIsError;
        private bool _isToastVisible;
        private bool _isRefreshing;
        private CancellationTokenSource _autoRefresh;
        private int _toastId;
        private TaskCompletionSource<bool> _dialog;
        private bool _isDialogOpen;
        private DateTime _lastActivity;
        private CancellationTokenSource _idleWatch;
        private readonly SynchronizationContext _uiContext;

        public MainViewModel(IUiHost ui)
        {
            Ui = ui;
            Profiles = new ProfileStore();
            Wallet = NewWallet(Profiles.VaultPath(Profiles.Active));
            Market = new MarketService();
            Settings = AppSettings.Load();
            Sounds.Enabled = () => Settings.SoundsOn;
            Performance.LiteSource = () => Settings.LiteMode;
            Lookup.WalletIcon = ThemeCatalog.Get(Settings.Theme).Key == "light" ? "walleticonblack" : "walleticonwhite";
            Settings.ApplyNetwork();
            if (Loc.Enabled) Loc.Instance.Current = Settings.Language;
            Loc.Changed += OnLanguageChanged;
            News = new NewsService();
            History = PaymentWatcher.Instance.History;
            Contacts = new ContactStore();
            Alerts = new AlertStore();
            Snapshots = new SnapshotStore();
            Swap = new SwapService();
            Swaps = new SwapStore();
            Passkey = new PasskeyStore(PasskeyStore.PathFor(Profiles.Active == null ? null : Profiles.Active.Id));
            SwitchProfileCommand = new RelayCommand(p => SwitchProfile(p as Profile));
            AddWalletCommand = new RelayCommand(StartAddWallet);
            AddWatchWalletCommand = new RelayCommand(StartWatchWallet);
            ToggleProfilesCommand = new RelayCommand(() => IsProfilesOpen = !IsProfilesOpen);
            Guard = new ClipboardGuard(text => Wallet.Chains.Any(c => SafeIsAddress(c, text))) { Enabled = Settings.ClipboardGuard };
            RefreshCommand = new AsyncCommand(RefreshAllAsync);
            ToggleHideCommand = new RelayCommand(() => HideBalances = !HideBalances);
            Notifications = PaymentWatcher.Instance.Notifications;
            _uiContext = SynchronizationContext.Current;
            PaymentWatcher.Instance.Received += OnWatcherReceived;
            NotificationList = new ObservableCollection<NotificationItem>();
            ToggleNotificationsCommand = new RelayCommand(() => IsNotificationsOpen = !IsNotificationsOpen);
            MarkAllReadCommand = new RelayCommand(() =>
            {
                Notifications.MarkAllRead();
                RebuildNotifications(false);
            });
            ClearNotificationsCommand = new RelayCommand(() =>
            {
                Notifications.Clear();
                RebuildNotifications(false);
            });
            OpenNotificationCommand = new RelayCommand(p => OpenNotification(p as NotificationItem));
            DismissNotificationCommand = new RelayCommand(p =>
            {
                var item = p as NotificationItem;
                if (item == null) return;
                Notifications.Remove(item.Notification.Id);
                RebuildNotifications(false);
            });
            ToggleNavCommand = new RelayCommand(() => IsNavCollapsed = !IsNavCollapsed);
            NavigateCommand = new RelayCommand(p =>
            {
                var key = p as string;
                if (key != null && IsUnlocked && NavItems.Any(n => n.Key == key)) Navigate(key);
            });
            _lastActivity = DateTime.UtcNow;
            Ui.ApplyTheme(ThemeCatalog.Get(Settings.Theme).Colors);
            AllAssets = new List<AssetItem>();
            MarketCoins = new MarketCoin[0];
            DialogTitle = "";
            DialogMessage = "";
            DialogConfirmText = "";
            AsyncCommand.ErrorHandler = e => ShowToast(Friendly(e), true);

            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem("home", "\uE80F", "Home"),
                new NavItem("market", "\uEAFC", "Market"),
                new NavItem("send", "\uE724", "Send"),
                new NavItem("receive", "\uE896", "Receive"),
                new NavItem("trade", "\uE8AB", "Trade"),
                new NavItem("activity", "\uE81C", "Activity"),
                new NavItem("settings", "\uE713", "Settings"),
            };
            LockCommand = new RelayCommand(Lock);
            DonateCommand = new RelayCommand(ShowDonate);
            DismissToastCommand = new RelayCommand(() => IsToastVisible = false);
            ToggleSupportCommand = new RelayCommand(() => IsSupportOpen = !IsSupportOpen);
            SupportTelegramCommand = new RelayCommand(() => { IsSupportOpen = false; Ui.OpenUrl("https://t.me/sillycs"); });
            SupportDiscordCommand = new RelayCommand(() => { IsSupportOpen = false; Ui.OpenUrl("https://discord.gg/NBMWgX9Crq"); });
            OpenRatonCommand = new RelayCommand(() => Ui.OpenUrl("https://raton.fun"));
            OpenAlternativeCommand = new RelayCommand(() => Ui.OpenUrl("https://alternative.me/crypto/fear-and-greed-index/"));
            OpenCoinGeckoCommand = new RelayCommand(() => Ui.OpenUrl("https://www.coingecko.com"));
            DialogConfirmCommand = new RelayCommand(() => CloseDialog(true));
            DialogCancelCommand = new RelayCommand(() => CloseDialog(false));

            ShowStart();
        }

        public IUiHost Ui { get; }
        public WalletManager Wallet { get; private set; }
        public ProfileStore Profiles { get; }
        public PasskeyStore Passkey { get; private set; }
        public RelayCommand SwitchProfileCommand { get; }
        public RelayCommand AddWalletCommand { get; }
        public RelayCommand AddWatchWalletCommand { get; }
        public RelayCommand ToggleProfilesCommand { get; }
        private Profile _pendingProfile;
        private bool _isProfilesOpen;

        public Profile ActiveProfile => Profiles.Active;
        private WalletManager NewWallet(string vaultPath)
        {
            var wallet = new WalletManager(vaultPath);
            wallet.LedgerConnect = () => Ui.ConnectLedgerAsync();
            return wallet;
        }

        public IReadOnlyList<Profile> ProfileList => Profiles.Items.ToList();

        public IReadOnlyList<ProfileChoice> ProfileChoices
        {
            get
            {
                var activeId = ActiveProfile == null ? null : ActiveProfile.Id;
                return Profiles.Items.Select(p => new ProfileChoice(p, p.Id == activeId ? ActiveProfilePhoto : p.PhotoPath)).ToList();
            }
        }
        public bool HasManyProfiles => Profiles.Items.Count > 1;
        public bool IsWatchOnly => Wallet.IsWatchOnly;
        public bool IsAddingWallet => _pendingProfile != null || AuthPage is WatchWalletViewModel;
        public string ActiveProfileName => ActiveProfile == null ? Loc.T("Wallet") : ActiveProfile.Name;
        public string ActiveProfileInitial => ActiveProfile == null ? "W" : ActiveProfile.Initial;

        public bool IsProfilesOpen
        {
            get { return _isProfilesOpen; }
            set { Set(ref _isProfilesOpen, value); }
        }

        public async Task ChooseProfilePhotoAsync(Profile profile)
        {
            if (profile != null) await ChoosePhotoAsync(profile.Id);
        }

        public async Task ChoosePhotoAsync(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var data = await Ui.PickImageAsync();
            if (data == null) return;
            if (!Sillet.Storage.AvatarStore.Save(key, data))
            {
                Notify("security", Loc.T("Photo not saved"), Loc.T("Pick a PNG or JPEG smaller than 3 MB"), null);
                return;
            }
            RaiseProfiles();
        }

        public void RemoveProfilePhoto(Profile profile)
        {
            if (profile != null) RemovePhoto(profile.Id);
        }

        public void RemovePhoto(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Sillet.Storage.AvatarStore.Remove(key);
            RaiseProfiles();
        }

        public string AccountPhotoKey(int index)
        {
            return "acc_" + (ActiveProfile == null ? "main" : ActiveProfile.Id) + "_" + index;
        }

        public string ActiveProfilePhoto
        {
            get
            {
                var account = Accounts == null ? null : Sillet.Storage.AvatarStore.PathFor(AccountPhotoKey(Accounts.Active));
                return account ?? (ActiveProfile == null ? null : ActiveProfile.PhotoPath);
            }
        }
        public bool ActiveProfileHasPhoto => ActiveProfilePhoto != null;
        public event Action PhotosChanged;

        private void RaiseProfiles()
        {
            Raise(nameof(ActiveProfilePhoto), nameof(ActiveProfileHasPhoto));
            var changed = PhotosChanged;
            if (changed != null) changed();
            Raise(nameof(ActiveProfile), nameof(ProfileList), nameof(ProfileChoices), nameof(HasManyProfiles), nameof(IsWatchOnly), nameof(ActiveProfileName),
                  nameof(ActiveProfileInitial), nameof(IsAddingWallet));
        }

        public void ShowStart()
        {
            var profile = Profiles.Active;
            if (profile != null && profile.IsWatch) ShowUnlock();
            else if (Wallet.HasSavedWallet) ShowUnlock();
            else ShowOnboarding(false);
        }

        private void UseProfile(Profile profile)
        {
            Profiles.SetActive(profile);
            Wallet = NewWallet(Profiles.VaultPath(profile));
            Passkey = new PasskeyStore(PasskeyStore.PathFor(profile == null ? null : profile.Id));
            Raise(nameof(Wallet), nameof(Passkey));
            RaiseProfiles();
        }

        public void SwitchProfile(Profile profile)
        {
            IsProfilesOpen = false;
            if (profile == null) return;
            if (IsUnlocked && ActiveProfile != null && ActiveProfile.Id == profile.Id) return;
            _pendingProfile = null;
            if (IsUnlocked) Lock();
            UseProfile(profile);
            ShowStart();
        }

        public void StartAddWallet()
        {
            IsProfilesOpen = false;
            if (IsUnlocked) Lock();
            _pendingProfile = Profiles.NewSeed();
            Wallet = NewWallet(Profiles.VaultPath(_pendingProfile));
            Passkey = new PasskeyStore(PasskeyStore.PathFor(_pendingProfile.Id));
            Raise(nameof(Wallet), nameof(Passkey));
            ShowOnboarding(false);
            RaiseProfiles();
        }

        public void StartWatchWallet()
        {
            IsProfilesOpen = false;
            if (IsUnlocked) Lock();
            _pendingProfile = null;
            AuthPage = new WatchWalletViewModel(this);
            RaiseProfiles();
        }

        public void CancelAddWallet()
        {
            _pendingProfile = null;
            if (Profiles.Active == null)
            {
                Wallet = new WalletManager(Profiles.VaultPath(null));
                ShowOnboarding(false);
                RaiseProfiles();
                return;
            }
            UseProfile(Profiles.Active);
            ShowStart();
        }

        public void OpenWatchProfile(string name, IDictionary<string, string> addresses)
        {
            var profile = Profiles.NewWatch(name, addresses);
            var wallet = new WalletManager(Profiles.VaultPath(profile));
            wallet.OpenWatch(profile.Watch);
            Profiles.Add(profile);
            Profiles.SetActive(profile);
            Wallet = wallet;
            Passkey = new PasskeyStore(PasskeyStore.PathFor(profile.Id));
            Raise(nameof(Wallet), nameof(Passkey));
            OnUnlocked();
        }

        public void OpenWatchActive()
        {
            var profile = Profiles.Active;
            if (profile == null || !profile.IsWatch) return;
            Wallet.OpenWatch(profile.Watch);
            OnUnlocked();
        }

        public void RenameProfile(Profile profile, string name)
        {
            Profiles.Rename(profile, name);
            RaiseProfiles();
        }

        public void RemoveProfileAfterDelete()
        {
            var active = Profiles.Active;
            if (active != null)
            {
                new PasskeyStore(PasskeyStore.PathFor(active.Id)).Remove(Ui);
                Profiles.Remove(active);
            }
            if (Profiles.Active != null) UseProfile(Profiles.Active);
            else
            {
                Wallet = new WalletManager(Profiles.VaultPath(null));
                Passkey = new PasskeyStore(PasskeyStore.PathFor(null));
                Raise(nameof(Wallet), nameof(Passkey));
                RaiseProfiles();
            }
        }

        public void RemoveWatchProfile(Profile profile)
        {
            if (profile == null || !profile.IsWatch) return;
            var wasActive = ActiveProfile != null && ActiveProfile.Id == profile.Id;
            if (wasActive && IsUnlocked) Lock();
            Profiles.Remove(profile);
            if (wasActive)
            {
                if (Profiles.Active != null) UseProfile(Profiles.Active);
                else
                {
                    Wallet = new WalletManager(Profiles.VaultPath(null));
                    RaiseProfiles();
                }
                ShowStart();
            }
            RaiseProfiles();
        }
        public MarketService Market { get; }
        public AppSettings Settings { get; }
        public NewsService News { get; }
        public HistoryStore History { get; }
        public ContactStore Contacts { get; }
        public AlertStore Alerts { get; }
        public SnapshotStore Snapshots { get; }
        public SwapService Swap { get; }
        public SwapStore Swaps { get; }
        public ClipboardGuard Guard { get; }

        private static bool SafeIsAddress(IChain chain, string text)
        {
            try
            {
                return text.Length >= 20 && text.Length <= 120 && chain.IsValidAddress(text);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void OnClipboardChanged(string text)
        {
            var warning = Guard.Observe(text, DateTime.UtcNow);
            if (warning == null) return;
            var ignored = ConfirmAsync(Loc.T("Clipboard changed"), warning, Loc.T("I understand"), true);
        }

        public void CopyText(string text)
        {
            Guard.ExpectCopy(text, DateTime.UtcNow);
            Ui.CopyToClipboard(text);
            if (Settings.AutoClearClipboard) ClearLaterAsync(text);
        }

        private async void ClearLaterAsync(string text)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            Ui.ClearClipboardIf(text);
        }
        public AsyncCommand RefreshCommand { get; }
        public RelayCommand ToggleHideCommand { get; }
        public RelayCommand ToggleNotificationsCommand { get; }
        public RelayCommand MarkAllReadCommand { get; }
        public RelayCommand ClearNotificationsCommand { get; }
        public RelayCommand DismissNotificationCommand { get; }
        public RelayCommand OpenNotificationCommand { get; }
        public RelayCommand ToggleNavCommand { get; }
        public RelayCommand NavigateCommand { get; }
        public NotificationStore Notifications { get; }
        public ObservableCollection<NotificationItem> NotificationList { get; }
        private bool _isNotificationsOpen;

        public bool IsNavCollapsed
        {
            get { return Settings.NavCollapsed; }
            set
            {
                if (Settings.NavCollapsed == value) return;
                Settings.NavCollapsed = value;
                Settings.Save();
                Raise(nameof(IsNavCollapsed), nameof(IsNavExpanded));
            }
        }

        public bool IsNavExpanded => !Settings.NavCollapsed;

        public int UnreadCount => Notifications.UnreadCount;
        public bool HasUnread => UnreadCount > 0;
        public bool HasNotifications => NotificationList.Count > 0;

        public bool IsNotificationsOpen
        {
            get { return _isNotificationsOpen; }
            set
            {
                if (!Set(ref _isNotificationsOpen, value) || !value) return;
                RebuildNotifications(true);
                Notifications.MarkAllRead();
                Raise(nameof(UnreadCount), nameof(HasUnread));
            }
        }

        private void RebuildNotifications(bool keepNew)
        {
            var now = DateTime.UtcNow;
            NotificationList.Clear();
            foreach (var n in Notifications.Items) NotificationList.Add(new NotificationItem(n, keepNew && !n.IsRead, now));
            Raise(nameof(UnreadCount), nameof(HasUnread), nameof(HasNotifications));
        }

        private void OpenNotification(NotificationItem item)
        {
            if (item == null) return;
            IsNotificationsOpen = false;
            var target = item.Notification.Target ?? "";
            if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Ui.OpenUrl(target);
            else if (IsUnlocked && NavItems.Any(n => n.Key == target)) Navigate(target);
        }

        public void RefreshNotificationList()
        {
            RebuildNotifications(false);
        }

        public void Notify(string kind, string title, string message, string target)
        {
            if (kind == "payment") Sounds.Play(title == Loc.T("Payment failed") ? Sounds.Error : Sounds.Payment);
            if (!Settings.NotificationsOn) return;
            Notifications.Add(kind, title, message, target, DateTime.UtcNow);
            if (IsNotificationsOpen) RebuildNotifications(true);
            else Raise(nameof(UnreadCount), nameof(HasUnread), nameof(HasNotifications));
            if (Settings.PushNotifications) Ui.ShowSystemNotification(title, message, kind);
        }

        private void OnWatcherReceived()
        {
            Action apply = () =>
            {
                if (Activity != null) Activity.Reload();
                if (IsNotificationsOpen) RebuildNotifications(true);
                else Raise(nameof(UnreadCount), nameof(HasUnread), nameof(HasNotifications));
                if (IsUnlocked) RefreshAfterPaymentAsync();
            };
            if (_uiContext != null) _uiContext.Post(_ => apply(), null);
            else apply();
        }

        private async void RefreshAfterPaymentAsync()
        {
            try
            {
                await RefreshBalancesAsync();
            }
            catch (Exception e) when (e is WalletException || e is System.Net.Http.HttpRequestException || e is TaskCanceledException)
            {
            }
        }

        public async Task CheckPaymentsAsync()
        {
            var readings = AllAssets
                .Where(a => a.BalanceLoaded && a.BalanceError == null && a.Chain.Address != null)
                .Select(a => new BalanceReading(a.Chain.Id + ":" + a.Chain.Address + ":" + a.Symbol, a.Symbol, a.Network, a.Chain.PaymentWatchBalance(a.Asset) ?? a.Balance))
                .ToList();
            var received = Notifications.DetectPayments(readings);
            PaymentWatcher.Instance.NoteAppPoll();
            var details = new Dictionary<string, KeyValuePair<TxStage, string>>();
            foreach (var r in received)
            {
                var asset = AllAssets.FirstOrDefault(a => a.Chain.Id == r.ChainId && a.Symbol == r.Symbol);
                var entry = await PaymentWatcher.MakeIncomingAsync(r, asset == null ? null : asset.Chain, asset == null ? null : asset.Asset);
                History.Add(entry);
                details[r.Key] = new KeyValuePair<TxStage, string>(await TxStatus.StageOfAsync(asset == null ? null : asset.Chain, entry.TxId), entry.From);
            }
            if (received.Count > 0 && Activity != null) Activity.Reload();
            if (!Settings.NotifyPayments) return;
            foreach (var r in received.Take(5))
            {
                KeyValuePair<TxStage, string> info;
                if (!details.TryGetValue(r.Key, out info)) info = new KeyValuePair<TxStage, string>(TxStage.Unknown, "");
                var message = (HideBalances ? Loc.F("{0} arrived on {1}", r.Symbol, r.Network) : Loc.F("{0} arrived on {1}", Formatters.Crypto(r.Balance) + " " + r.Symbol, r.Network))
                    + "\n" + TxStatus.ReceivedLine(info.Key, info.Value);
                Notify("payment", TxStatus.ReceivedTitle(info.Key), message, "activity");
            }
        }

        public async Task CheckNewsAsync()
        {
            IReadOnlyList<NewsItem> news;
            try
            {
                news = await News.GetAsync();
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is TaskCanceledException || e is System.IO.IOException)
            {
                return;
            }
            var fresh = Notifications.DetectNews(news);
            if (!Settings.NotifyNews) return;
            foreach (var n in fresh.Skip(Math.Max(0, fresh.Count - 3)))
                Notify("news", n.HasTitle ? n.Title : Loc.F("News from {0}", n.Author), n.Description, n.HasUrl ? n.Url : "home");
        }

        public void CheckBitcoin()
        {
            if (!Settings.NotifyBitcoin || Market.IsStale) return;
            PriceMoves.Check(Notifications, MarketCoins, Settings, (kind, title, message) => Notify(kind, title, message, "market"));
        }
        public RelayCommand OpenAlternativeCommand { get; }
        public RelayCommand OpenCoinGeckoCommand { get; }

        public ObservableCollection<NavItem> NavItems { get; }

        public object AuthPage
        {
            get { return _authPage; }
            private set { Set(ref _authPage, value); }
        }

        public object CurrentPage
        {
            get { return _currentPage; }
            private set
            {
                if (Set(ref _currentPage, value)) Raise(nameof(IsDonatePage));
            }
        }

        public bool IsUnlocked
        {
            get { return _isUnlocked; }
            private set { Set(ref _isUnlocked, value); }
        }

        public NavItem SelectedNav
        {
            get { return _selectedNav; }
            set
            {
                if (!Set(ref _selectedNav, value) || value == null) return;
                if (Activity != null && !_historyNav) Activity.ClearAssetFilter();
                switch (value.Key)
                {
                    case "market": CurrentPage = Markets; break;
                    case "send": CurrentPage = Send; break;
                    case "receive": CurrentPage = Receive; break;
                    case "trade":
                        CurrentPage = Trade;
                        if (Trade != null)
                        {
                            var ignoredSwaps = Trade.RefreshSwapsAsync();
                        }
                        break;
                    case "activity":
                        if (Activity != null) Activity.Reload();
                        CurrentPage = Activity;
                        break;
                    case "settings": CurrentPage = SettingsPage; break;
                    default: CurrentPage = Home; break;
                }
            }
        }

        private string _pendingTarget;

        public void OpenTarget(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (IsUnlocked && NavItems.Any(n => n.Key == target))
            {
                _pendingTarget = null;
                IsNotificationsOpen = false;
                Navigate(target);
            }
            else if (!IsUnlocked) _pendingTarget = target;
        }

        public void Navigate(string key)
        {
            SelectedNav = NavItems.First(n => n.Key == key);
        }

        private bool _historyNav;

        public void ShowAssetHistory(string symbol, string chainId)
        {
            if (!IsUnlocked || Activity == null || string.IsNullOrEmpty(symbol)) return;
            _historyNav = true;
            try
            {
                Navigate("activity");
                Activity.ShowAsset(symbol, chainId);
            }
            finally
            {
                _historyNav = false;
            }
        }

        public HomeViewModel Home { get; private set; }
        public MarketViewModel Markets { get; private set; }
        public SendViewModel Send { get; private set; }
        public ReceiveViewModel Receive { get; private set; }
        public ActivityViewModel Activity { get; private set; }
        public TradeViewModel Trade { get; private set; }
        public SettingsViewModel SettingsPage { get; private set; }

        public RelayCommand LockCommand { get; }
        public RelayCommand DonateCommand { get; }
        public DonateViewModel Donate { get; private set; }
        public bool IsDonatePage => _currentPage != null && _currentPage == Donate;

        public void ShowDonate()
        {
            if (!IsUnlocked || Donate == null) return;
            _selectedNav = null;
            Raise(nameof(SelectedNav));
            CurrentPage = Donate;
        }
        public RelayCommand ToggleSupportCommand { get; }
        public RelayCommand SupportTelegramCommand { get; }
        public RelayCommand SupportDiscordCommand { get; }
        private bool _isSupportOpen;

        public bool IsSupportOpen
        {
            get { return _isSupportOpen; }
            set { Set(ref _isSupportOpen, value); }
        }

        public RelayCommand OpenRatonCommand { get; }

        public void ShowOnboarding(bool restoring)
        {
            AuthPage = new OnboardingViewModel(this, restoring);
        }

        public void ShowUnlock()
        {
            AuthPage = new UnlockViewModel(this);
        }

        private DateTime _unlockBlockedUntil = DateTime.MinValue;

        public int UnlockWaitSeconds(DateTime nowUtc)
        {
            if (!Settings.SlowDownUnlock || nowUtc >= _unlockBlockedUntil) return 0;
            return (int)Math.Ceiling((_unlockBlockedUntil - nowUtc).TotalSeconds);
        }

        public int RegisterFailedUnlock(DateTime nowUtc)
        {
            Settings.FailedUnlocks++;
            Settings.Save();
            if (!Settings.SlowDownUnlock || Settings.FailedUnlocks < 3) return 0;
            var seconds = (int)Math.Min(300, 5 * Math.Pow(2, Math.Min(10, Settings.FailedUnlocks - 3)));
            _unlockBlockedUntil = nowUtc.AddSeconds(seconds);
            return seconds;
        }

        public string LastOpenedText
        {
            get
            {
                DateTime last;
                if (!DateTime.TryParse(Settings.LastUnlockUtc, Formatters.Display, System.Globalization.DateTimeStyles.RoundtripKind, out last)) return "";
                return Loc.F("Last opened {0}", last.ToLocalTime().ToString("MMM d, HH:mm", Loc.Instance.Culture));
            }
        }

        public void OnUnlocked()
        {
            if (_pendingProfile != null)
            {
                Profiles.Add(_pendingProfile);
                Profiles.SetActive(_pendingProfile);
                _pendingProfile = null;
            }
            else Profiles.EnsureMain();
            Accounts = new AccountStore(AccountStore.PathFor(Profiles.Active == null ? null : Profiles.Active.Id));
            if (!Wallet.IsWatchOnly && Wallet.Account != Accounts.Active) Wallet.SetAccount(Accounts.Active);
            LoadTokens();
            if (!Wallet.IsWatchOnly && Accounts.Active == 0) Profiles.SetAddresses(Profiles.Active, Wallet.PublicAddresses());
            RaiseProfiles();
            var failed = Settings.FailedUnlocks;
            Settings.FailedUnlocks = 0;
            Settings.LastUnlockUtc = DateTime.UtcNow.ToString("o", Formatters.Display);
            Settings.Save();
            _unlockBlockedUntil = DateTime.MinValue;
            BuildPages();
            IsUnlocked = true;
            AuthPage = null;
            Sounds.Play(Sounds.Unlocked);
            var runVersion = AppInfo.CurrentVersion;
            if (!string.IsNullOrEmpty(Settings.LastRunVersion) && Settings.LastRunVersion != runVersion)
            {
                Settings.LastRunVersion = runVersion;
                Settings.Save();
                var laterSound = Task.Delay(1400).ContinueWith(t => Sounds.Play(Sounds.Updated));
            }
            else if (Settings.LastRunVersion != runVersion)
            {
                Settings.LastRunVersion = runVersion;
                Settings.Save();
            }
            _selectedNav = null;
            Navigate(NavItems.Any(n => n.Key == Settings.StartPage) ? Settings.StartPage : "home");
            if (_pendingTarget != null) { var wanted = _pendingTarget; _pendingTarget = null; if (NavItems.Any(n => n.Key == wanted)) Navigate(wanted); }
            StartAutoRefresh();
            StartIdleWatch();
            RemindBackup();
            var ignoredUpdate = CheckUpdateAsync();
            if (failed > 0)
            {
                var ignoredWarning = ConfirmAsync(Loc.T("Wrong password attempts"),
                    failed == 1 ? Loc.T("1 wrong password since your last visit. Not you? Change your password") : Loc.F("{0} wrong passwords since your last visit. Not you? Change your password", failed),
                    Loc.T("Got it"), false);
            }
        }

        public TokenStore Tokens { get; private set; }

        private IEnumerable<string> BuiltInSymbols()
        {
            return Wallet.Chains.SelectMany(c => c.Assets).Where(a => !a.IsCustom).Select(a => a.Symbol).ToList();
        }

        private void LoadTokens()
        {
            Tokens = new TokenStore();
            var reserved = BuiltInSymbols().ToList();
            foreach (var t in Tokens.Items)
            {
                var chain = Wallet.Chains.FirstOrDefault(c => c.Id == t.ChainId && c.SupportsCustomTokens);
                if (chain == null) continue;
                try
                {
                    chain.AddCustomAsset(new Asset(TokenStore.SafeSymbol(t.Symbol, reserved), t.Name, t.Decimals, t.Contract));
                }
                catch (WalletException)
                {
                }
            }
        }

        public void AddCustomToken(IChain chain, Asset asset)
        {
            var original = asset.Symbol;
            var added = new Asset(TokenStore.SafeSymbol(original, BuiltInSymbols()), asset.Name, asset.Decimals, asset.Contract);
            chain.AddCustomAsset(added);
            Tokens.Add(new CustomToken(chain.Id, asset.Contract, original, asset.Name, asset.Decimals));
            ReloadAfterTokens();
            ShowToast(Loc.F("{0} added to {1}", added.Symbol, chain.Name));
        }

        public void RemoveCustomToken(IChain chain, Asset asset)
        {
            chain.RemoveCustomAsset(asset);
            Tokens.Remove(chain.Id, asset.Contract);
            ReloadAfterTokens();
        }

        private void ReloadAfterTokens()
        {
            if (_autoRefresh != null) _autoRefresh.Cancel();
            BuildPages();
            _selectedNav = null;
            Navigate("settings");
            StartAutoRefresh();
        }

        public AccountStore Accounts { get; private set; }
        public int ActiveAccount => Accounts == null ? 0 : Accounts.Active;
        public string ActiveAccountName => Accounts == null ? "" : Accounts.Name(Accounts.Active);

        public void NotifyAccountChanged()
        {
            Raise(nameof(ActiveAccount), nameof(ActiveAccountName), nameof(ActiveProfilePhoto), nameof(ActiveProfileHasPhoto), nameof(ProfileChoices));
        }

        public void SwitchAccount(int index)
        {
            if (Accounts == null || Wallet.IsWatchOnly || index == Accounts.Active) return;
            Wallet.SetAccount(index);
            Accounts.SetActive(index);
            if (_autoRefresh != null) _autoRefresh.Cancel();
            BuildPages();
            Raise(nameof(ActiveAccount), nameof(ActiveAccountName), nameof(ActiveProfilePhoto), nameof(ActiveProfileHasPhoto), nameof(ProfileChoices));
            _selectedNav = null;
            Navigate("home");
            StartAutoRefresh();
            ShowToast(Loc.F("Switched to {0}", ActiveAccountName));
        }

        private void BuildPages()
        {
            AllAssets = Wallet.Chains.SelectMany(c => c.Assets.Select(a => new AssetItem(c, a))).ToList();
            Home = new HomeViewModel(this);
            Markets = new MarketViewModel(this);
            Send = new SendViewModel(this);
            Receive = new ReceiveViewModel(this);
            Activity = new ActivityViewModel(this);
            Trade = new TradeViewModel(this);
            SettingsPage = new SettingsViewModel(this);
            Donate = new DonateViewModel(this);
            Raise(nameof(Home), nameof(Markets), nameof(Send), nameof(Receive), nameof(Activity), nameof(Trade), nameof(SettingsPage));
        }

        private void RemindBackup()
        {
            if (Wallet.IsWatchOnly || Settings.PhraseViewed || Settings.BackupReminded) return;
            Settings.BackupReminded = true;
            Settings.Save();
            Notify("security", Loc.T("Back up your recovery phrase"),
                Loc.T("It is the only way to get your money back if you lose this device. Write it down and keep it offline"), "settings");
        }

        public async Task<UpdateResult> CheckUpdateAsync(bool announceAlways = false)
        {
            var result = await UpdateService.RunAsync(Settings);
            if (result == null) return null;
            var update = result.Update;
            if (announceAlways || Settings.UpdateNotified != update.Version)
            {
                Settings.UpdateNotified = update.Version;
                Settings.Save();
                if (!result.Handled) Notify("update", Loc.T("Update available"), Loc.F("Sillet {0} is ready to download", update.Version), update.Url);
            }
            return result;
        }

        public void Lock()
        {
            if (_autoRefresh != null) _autoRefresh.Cancel();
            if (_idleWatch != null) _idleWatch.Cancel();
            DataChanged = null;
            CloseDialog(false);
            IsNotificationsOpen = false;
            Wallet.Lock();
            IsUnlocked = false;
            CurrentPage = null;
            Home = null;
            Markets = null;
            Send = null;
            Receive = null;
            Activity = null;
            Trade = null;
            SettingsPage = null;
            Donate = null;
            AllAssets = new List<AssetItem>();
            IsProfilesOpen = false;
            ShowStart();
        }

        public List<AssetItem> AllAssets { get; private set; }
        public IReadOnlyList<MarketCoin> MarketCoins { get; private set; }
        public FearGreed FearGreed { get; private set; }
        public DateTime? LastUpdate { get; private set; }

        public event Action DataChanged;

        public bool IsRefreshing
        {
            get { return _isRefreshing; }
            private set { Set(ref _isRefreshing, value); }
        }

        public string Currency
        {
            get { return Settings.Currency; }
            set
            {
                if (Settings.Currency == value) return;
                Settings.Currency = value;
                Settings.Save();
                Raise(nameof(Currency), nameof(CurrencyLabel));
                if (Donate != null) Donate.RefreshAmounts();
                var ignored = RefreshMarketsAsync(true);
            }
        }

        public string CurrencyLabel => Currency.ToUpperInvariant();

        public string LocalCurrency => _localCurrency ?? (_localCurrency = Currencies.Detect());
        private string _localCurrency;
        private string _marketsCurrency;

        public decimal PerUsd
        {
            get
            {
                var tether = MarketCoins == null ? null : MarketCoins.FirstOrDefault(c => c.Id == "tether");
                if (tether != null && tether.Price > 0 && _marketsCurrency == Currency) return tether.Price;
                return Currencies.Get(Currency).PerUsd;
            }
        }

        public decimal UsdToFiat(decimal usd)
        {
            return Currencies.Nice(usd * PerUsd);
        }

        public bool HideBalances
        {
            get { return Settings.HideBalances; }
            set
            {
                if (Settings.HideBalances == value) return;
                Settings.HideBalances = value;
                Settings.Save();
                Raise(nameof(HideBalances));
                NotifyDataChanged();
            }
        }

        public string Theme
        {
            get { return ThemeCatalog.Get(Settings.Theme).Key; }
            set
            {
                var theme = ThemeCatalog.Find(value);
                if (theme == null || theme.Key == Theme) return;
                Settings.Theme = theme.Key;
                Settings.Save();
                Ui.ApplyTheme(theme.Colors);
                Lookup.WalletIcon = WalletIconName;
                Raise(nameof(Theme), nameof(LogoName), nameof(WalletIconName), nameof(TelegramIconName), nameof(DiscordIconName));
                RaiseDataChanged();
            }
        }

        public string[] Palette => ThemeCatalog.Get(Settings.Theme).Palette;

        public string LogoName => Theme == "light" ? "silleticonblack" : "silleticon";
        public string WalletIconName => Theme == "light" ? "walleticonblack" : "walleticonwhite";
        public string TelegramIconName => Theme == "light" ? "telegramblack" : "telegramwhite";
        public string DiscordIconName => Theme == "light" ? "discordblack" : "discordwhite";

        public int AutoLockMinutes
        {
            get { return Settings.AutoLockMinutes; }
            set
            {
                if (Settings.AutoLockMinutes == value) return;
                Settings.AutoLockMinutes = Math.Max(0, value);
                Settings.Save();
                Touch();
                Raise(nameof(AutoLockMinutes));
            }
        }

        public int RefreshMinutes
        {
            get { return Settings.RefreshMinutes; }
            set
            {
                if (Settings.RefreshMinutes == value) return;
                Settings.RefreshMinutes = Math.Max(1, value);
                Settings.Save();
                Raise(nameof(RefreshMinutes));
                if (IsUnlocked) StartAutoRefresh();
            }
        }

        public bool HideSmallBalances
        {
            get { return Settings.HideSmallBalances; }
            set
            {
                if (Settings.HideSmallBalances == value) return;
                Settings.HideSmallBalances = value;
                Settings.Save();
                Raise(nameof(HideSmallBalances));
                RaiseDataChanged();
            }
        }

        public void RecordSent(PreparedTransfer transfer, TransferResult result)
        {
            History.Add(new HistoryEntry(DateTime.UtcNow, transfer.Chain.Id, transfer.Chain.Name, transfer.Asset.Symbol, transfer.Amount,
                transfer.Fee, transfer.FeeAsset.Symbol, transfer.Chain.Address, transfer.To, result.TxId, result.ExplorerUrl));
            if (Activity != null) Activity.Reload();
            if (Send != null) Send.LoadRecipients();
        }

        public string AllAddressesText()
        {
            return string.Join(Environment.NewLine, Wallet.Chains.Select(c => c.Name + ": " + c.Address));
        }

        public void Touch()
        {
            _lastActivity = DateTime.UtcNow;
        }

        public bool IsIdleExpired(DateTime nowUtc)
        {
            return IsUnlocked && AutoLockMinutes > 0 && nowUtc - _lastActivity >= TimeSpan.FromMinutes(AutoLockMinutes);
        }

        private void StartIdleWatch()
        {
            if (_idleWatch != null) _idleWatch.Cancel();
            var cts = new CancellationTokenSource();
            _idleWatch = cts;
            Touch();
            var ignored = IdleLoopAsync(cts.Token);
        }

        private async Task IdleLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    if (IsIdleExpired(DateTime.UtcNow) && !IsDialogOpen)
                    {
                        Lock();
                        ShowToast(Loc.T("Locked after inactivity"));
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void NotifyDataChanged()
        {
            foreach (var a in AllAssets) a.UpdateTexts(Currency, HideBalances);
            RaiseDataChanged();
        }

        private int _dataPending;

        private void RaiseDataChanged()
        {
            var ctx = _uiContext;
            if (ctx == null)
            {
                var handler = DataChanged;
                if (handler != null) handler();
                return;
            }
            if (Interlocked.Exchange(ref _dataPending, 1) == 1) return;
            var ignored = Task.Delay(180).ContinueWith(t => ctx.Post(_ =>
            {
                Interlocked.Exchange(ref _dataPending, 0);
                var handler = DataChanged;
                if (handler != null) handler();
            }, null));
        }

        public async Task RefreshAllAsync()
        {
            if (IsRefreshing || !IsUnlocked) return;
            IsRefreshing = true;
            try
            {
                await Task.WhenAll(RefreshMarketsAsync(), RefreshBalancesAsync(), RefreshFearGreedAsync(), CheckNewsAsync());
                RecordSnapshot(DateTime.Now);
                if (Trade != null) await Trade.RefreshSwapsAsync();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        public void RecordSnapshot(DateTime now)
        {
            if (!IsUnlocked || AllAssets.Count == 0 || !AllAssets.Any(a => a.BalanceLoaded)) return;
            if (AllAssets.Any(a => a.HasBalance && a.CoinId != null && a.Price <= 0)) return;
            Snapshots.Record(Currency, AllAssets.Sum(a => a.FiatValue), now);
            RaiseDataChanged();
        }

        public IReadOnlyList<ChartPoint> PortfolioPoints => Snapshots.Points(Currency);

        public void CheckAlerts()
        {
            foreach (var alert in Alerts.Check(MarketCoins, Currency))
            {
                ShowToast(alert.Message);
                Notify("alert", Loc.T("Price alert"), alert.Message, "market");
            }
            if (Markets != null) Markets.ReloadAlerts();
        }

        public async Task RefreshMarketsAsync(bool force = false)
        {
            try
            {
                var marketsCurrency = Currency;
                MarketCoins = await Market.GetMarketsAsync(marketsCurrency, force);
                _marketsCurrency = marketsCurrency;
                if (Market.IsStale) ShowToast(Loc.T("Offline. Showing saved prices"), false);
            }
            catch (MarketException e)
            {
                ShowToast(Clean(e.Message), true);
                return;
            }
            var byId = new Dictionary<string, MarketCoin>();
            foreach (var coin in MarketCoins) byId[coin.Id] = coin;
            foreach (var a in AllAssets)
            {
                MarketCoin c;
                if (a.CoinId != null && byId.TryGetValue(a.CoinId, out c))
                    a.SetMarket(c.Price, c.Change24h, c.Name);
            }
            NotifyDataChanged();
            CheckAlerts();
            CheckBitcoin();
            var ignored = DownloadIconsAsync();
        }

        public async Task RefreshBalancesAsync()
        {
            var portfolio = await Wallet.GetPortfolioAsync();
            foreach (var entry in portfolio)
            {
                var item = AllAssets.FirstOrDefault(a => a.Chain == entry.Chain && a.Asset == entry.Asset);
                if (item != null) item.SetBalance(entry.Balance == null ? (decimal?)null : entry.Balance.Amount, entry.Error);
            }
            LastUpdate = DateTime.Now;
            NotifyDataChanged();
            await CheckPaymentsAsync();
        }

        private async Task RefreshFearGreedAsync()
        {
            FearGreed = await Market.GetFearGreedAsync() ?? FearGreed;
            RaiseDataChanged();
        }

        private async Task DownloadIconsAsync()
        {
            foreach (var coin in MarketCoins)
            {
                var path = await IconCache.GetAsync(coin.Id, coin.ImageUrl);
                if (path == null) continue;
                foreach (var a in AllAssets.Where(a => a.CoinId == coin.Id)) a.IconPath = path;
                if (Markets != null) Markets.SetIcon(coin.Id, path);
            }
        }

        private void StartAutoRefresh()
        {
            if (_autoRefresh != null) _autoRefresh.Cancel();
            var cts = new CancellationTokenSource();
            _autoRefresh = cts;
            var ignored = AutoRefreshLoopAsync(cts.Token);
        }

        private async Task AutoRefreshLoopAsync(CancellationToken ct)
        {
            await RefreshAllAsync();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, Settings.RefreshMinutes)), ct);
                    await RefreshAllAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public bool IsDialogOpen
        {
            get { return _isDialogOpen; }
            private set { Set(ref _isDialogOpen, value); }
        }

        public string DialogTitle { get; private set; }
        public string DialogMessage { get; private set; }
        public string DialogConfirmText { get; private set; }
        public bool DialogIsDanger { get; private set; }
        public RelayCommand DialogConfirmCommand { get; }
        public RelayCommand DialogCancelCommand { get; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool danger = false)
        {
            if (_dialog != null) _dialog.TrySetResult(false);
            _dialog = new TaskCompletionSource<bool>();
            DialogTitle = Loc.T(title);
            DialogMessage = Loc.T(message);
            DialogConfirmText = Loc.T(confirmText);
            DialogIsDanger = danger;
            Raise(nameof(DialogTitle), nameof(DialogMessage), nameof(DialogConfirmText), nameof(DialogIsDanger));
            IsDialogOpen = true;
            return _dialog.Task;
        }

        private void CloseDialog(bool result)
        {
            IsDialogOpen = false;
            if (_dialog != null) _dialog.TrySetResult(result);
            _dialog = null;
        }

        public string ToastMessage
        {
            get { return _toastMessage; }
            private set { Set(ref _toastMessage, value); }
        }

        public bool ToastIsError
        {
            get { return _toastIsError; }
            private set { Set(ref _toastIsError, value); }
        }

        public bool IsToastVisible
        {
            get { return _isToastVisible; }
            private set { Set(ref _isToastVisible, value); }
        }

        public RelayCommand DismissToastCommand { get; }

        public async void ShowToast(string message, bool isError = false)
        {
            int id = ++_toastId;
            ToastMessage = Clean(message);
            if (isError) Sounds.Play(Sounds.Error);
            ToastIsError = isError;
            IsToastVisible = true;
            await Task.Delay(isError ? 6000 : 3500);
            if (id == _toastId) IsToastVisible = false;
        }

        public static string Friendly(Exception e)
        {
            if (e is WalletException || e is MarketException) return Loc.T(Clean(e.Message));
            if (e is HttpRequestException) return Loc.T("No connection to the network. Check your internet");
            if (e is TaskCanceledException) return Loc.T("The network took too long to respond. Try again");
            return Loc.F("Unexpected error: {0}", Clean(e.Message));
        }

        private void OnLanguageChanged()
        {
            RaiseAll();
            foreach (var page in new ObservableObject[] { Home, Markets, Send, Receive, Activity, Trade, SettingsPage, Donate, AuthPage as ObservableObject })
                if (page != null) page.RaiseAll();
        }

        public static string Clean(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            var text = message.Trim();
            while (text.EndsWith(".") && !text.EndsWith("...")) text = text.Substring(0, text.Length - 1).TrimEnd();
            return Loc.T(text);
        }
    }
}

namespace Sillet.App.ViewModels
{
    public sealed class ProfileChoice
    {
        public ProfileChoice(Sillet.Storage.Profile profile, string photoPath) { Profile = profile; PhotoPath = photoPath; }
        public Sillet.Storage.Profile Profile { get; private set; }
        public string PhotoPath { get; private set; }
        public bool HasPhoto { get { return PhotoPath != null; } }
        public string Name { get { return Profile.Name; } }
        public string KindText { get { return Profile.KindText; } }
    }
}
