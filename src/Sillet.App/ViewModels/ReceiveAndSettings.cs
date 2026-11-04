using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.Core;

namespace Sillet.App.ViewModels
{
    public sealed class ReceiveViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private CoinGroup _selectedCoin;
        private AssetItem _selectedNetwork;
        private bool _copied;
        private int _copyId;

        public ReceiveViewModel(MainViewModel main)
        {
            _main = main;
            Coins = new ObservableCollection<CoinGroup>(Lookup.GroupByCoin(main.AllAssets));
            main.DataChanged += () => Raise(nameof(BalanceText), nameof(Address), nameof(ShowRotateBanner));
            CopyCommand = new RelayCommand(Copy);
            DismissRotateBannerCommand = new RelayCommand(() =>
            {
                main.Settings.RotateBannerDismissed = true;
                main.Settings.Save();
                Raise(nameof(ShowRotateBanner));
            });
            OpenRotateSettingsCommand = new RelayCommand(() =>
            {
                main.Settings.RotateBannerDismissed = true;
                main.Settings.Save();
                Raise(nameof(ShowRotateBanner));
                main.Navigate("settings");
            });
            OpenExplorerCommand = new RelayCommand(() =>
            {
                if (SelectedNetwork != null && Address.Length > 0) _main.Ui.OpenUrl(SelectedNetwork.Chain.ExplorerAddressUrl(Address));
            });
            SelectedCoin = Coins.FirstOrDefault();
        }

        public ObservableCollection<CoinGroup> Coins { get; }

        public CoinGroup SelectedCoin
        {
            get { return _selectedCoin; }
            set
            {
                if (!Set(ref _selectedCoin, value)) return;
                Raise(nameof(Networks), nameof(HasManyNetworks));
                SelectedNetwork = value == null ? null : value.Assets[0];
            }
        }

        public IReadOnlyList<AssetItem> Networks => SelectedCoin == null ? new AssetItem[0] : SelectedCoin.Assets;
        public bool HasManyNetworks => SelectedCoin != null && SelectedCoin.NetworkCount > 1;

        public AssetItem SelectedNetwork
        {
            get { return _selectedNetwork; }
            set
            {
                if (value == null && SelectedCoin != null) return;
                if (!Set(ref _selectedNetwork, value)) return;
                _copyId++;
                Copied = false;
                Raise(nameof(Address), nameof(ShowRotateBanner), nameof(Title), nameof(Subtitle), nameof(WarningText), nameof(CoinText), nameof(NetworkText),
                      nameof(PathText), nameof(BalanceText), nameof(SharedText), nameof(HasShared), nameof(TokenText));
            }
        }

        public RelayCommand DismissRotateBannerCommand { get; private set; }
        public RelayCommand OpenRotateSettingsCommand { get; private set; }
        public bool ShowRotateBanner => SelectedNetwork != null && SelectedNetwork.Chain is Sillet.Chains.Utxo.UtxoChain
            && !_main.Settings.RotateReceive && !_main.Settings.RotateBannerDismissed && !_main.IsWatchOnly;

        public string Address => SelectedNetwork == null ? "" : SelectedNetwork.Chain.ReceiveAddress ?? "";
        public string Title => SelectedNetwork == null ? "" : Loc.F("Receive {0}", SelectedNetwork.Symbol);
        public string Subtitle => SelectedNetwork == null ? "" : Loc.F("{0} on {1}", SelectedNetwork.DisplayName, SelectedNetwork.Network);

        public string WarningText => SelectedNetwork == null
            ? ""
            : Loc.F("Only send {0} on {1} or funds may be lost", SelectedNetwork.Symbol, SelectedNetwork.Network);

        public bool Copied
        {
            get { return _copied; }
            private set { Set(ref _copied, value); }
        }

        public RelayCommand CopyCommand { get; }
        public RelayCommand OpenExplorerCommand { get; }
        public string CoinText => SelectedNetwork == null ? "" : Lookup.CoinName(SelectedNetwork.Symbol) + " (" + SelectedNetwork.Symbol + ")";
        public string NetworkText => SelectedNetwork == null ? "" : SelectedNetwork.Network;
        public string PathText => SelectedNetwork == null ? "" : SelectedNetwork.Chain.DerivationPath;
        public string TokenText => SelectedNetwork == null ? "" : SelectedNetwork.IsToken ? Loc.F("Token, fees paid in {0}", SelectedNetwork.Chain.Native.Symbol) : Loc.T("Native coin");

        public string BalanceText
        {
            get
            {
                if (SelectedNetwork == null) return "";
                if (_main.HideBalances) return Formatters.Hidden;
                return SelectedNetwork.BalanceLoaded ? Formatters.Crypto(SelectedNetwork.Balance) + " " + SelectedNetwork.Symbol : "-";
            }
        }

        public string SharedText
        {
            get
            {
                if (SelectedNetwork == null) return "";
                var others = _main.Wallet.Chains.Where(c => c != SelectedNetwork.Chain && c.Address == SelectedNetwork.Chain.Address).Select(c => c.Name).ToList();
                return others.Count == 0 ? "" : string.Join(", ", others);
            }
        }

        public bool HasShared => SharedText.Length > 0;

        public void Select(string symbol, string network)
        {
            var coin = Coins.FirstOrDefault(c => c.Symbol == symbol);
            if (coin == null) return;
            SelectedCoin = coin;
            var asset = coin.Assets.FirstOrDefault(a => a.Network == network);
            if (asset != null) SelectedNetwork = asset;
        }

        private async void Copy()
        {
            if (Address.Length == 0) return;
            _main.CopyText(Address);
            int id = ++_copyId;
            Copied = true;
            await Task.Delay(2000);
            if (id == _copyId) Copied = false;
        }
    }

    public sealed class CurrencyRow
    {
        public CurrencyRow(CurrencyInfo info, bool isSelected)
        {
            Info = info;
            IsSelected = isSelected;
        }

        public CurrencyInfo Info { get; }
        public bool IsSelected { get; }
        public string Title => Info.Title;
        public string Description => Info.Description;
        public string IconText => Info.IconText;
        public string FlagPath => Flags.PathFor(Info.Code);
        public string FlagEmoji => Flags.Emoji(Info.Code);
        public bool HasFlag => FlagPath != null;
        public bool ShowEmoji => FlagPath == null && FlagEmoji.Length > 0;
        public bool ShowText => FlagPath == null && FlagEmoji.Length == 0;
    }

    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private bool _isRevealed;
        private string _revealError;
        private string _deleteError;
        private string _passwordError;

        public SettingsViewModel(MainViewModel main)
        {
            _main = main;
            Words = new ObservableCollection<WordItem>();
            Wallets = new ObservableCollection<ProfileRow>();
            AccountRows = new ObservableCollection<AccountRow>();
            ReloadWallets();
            ReloadAccounts();
            main.PhotosChanged += () =>
            {
                foreach (var w in Wallets) w.RefreshPhoto();
                foreach (var a in AccountRows) a.RefreshPhoto();
            };
            AddAccountCommand = new RelayCommand(AddAccount);
            ToggleDeveloperCommand = new RelayCommand(() => IsDeveloperOpen = !IsDeveloperOpen);
            DevFakeSentCommand = new RelayCommand(DevFakeSent);
            DevFakeReceivedCommand = new AsyncCommand(DevFakeReceivedAsync);
            DevNotificationsCommand = new RelayCommand(DevNotifications);
            DevReceiptCommand = new RelayCommand(DevReceipt);
            DevClearCommand = new RelayCommand(DevClear);
            DevBigPaymentCommand = new RelayCommand(DevBigPayment);
            DevManyEntriesCommand = new RelayCommand(DevManyEntries);
            TokenRows = new ObservableCollection<TokenRow>();
            ReloadTokens();
            _proxyText = _main.Settings.ProxyUrl ?? "";
            _endpointsText = _main.Settings.CustomEndpoints ?? "";
            SaveNetworkCommand = new RelayCommand(SaveNetwork);
            UseTorCommand = new RelayCommand(() => ProxyText = "socks5://127.0.0.1:9050");
            LookupTokenCommand = new AsyncCommand(LookupTokenAsync);
            AddTokenCommand = new RelayCommand(AddFoundToken, () => _foundToken != null);
            RevealPassword = "";
            DeletePassword = "";
            RevealCommand = new AsyncCommand(RevealAsync);
            HideWordsCommand = new RelayCommand(() =>
            {
                Words.Clear();
                IsRevealed = false;
            });
            DeleteCommand = new AsyncCommand(DeleteAsync);
            CheckUpdateCommand = new AsyncCommand(CheckUpdateNowAsync);
            RestartCommand = new RelayCommand(() => _main.Ui.RestartApp());
            OpenCoinGeckoCommand = _main.OpenCoinGeckoCommand;
            OpenAlternativeCommand = _main.OpenAlternativeCommand;
            CurrentPassword = "";
            NewPassword = "";
            ChangePasswordCommand = new AsyncCommand(ChangePasswordAsync);
            SetupPasskeyCommand = new AsyncCommand(SetupPasskeyAsync);
            TestPushCommand = new RelayCommand(() =>
            {
                _main.Ui.ShowSystemNotification("Sillet", Loc.T("Notifications are working"));
                _main.ShowToast(Loc.T("Test notification sent"));
            });
            RemovePasskeyCommand = new RelayCommand(RemovePasskey);
            PasskeyPassword = "";
            CopyAddressesCommand = new RelayCommand(() =>
            {
                _main.CopyText(_main.AllAddressesText());
                _main.ShowToast(Loc.T("All addresses copied"));
            });
            ClearCacheCommand = new RelayCommand(ClearCache);
            OpenDataFolderCommand = new RelayCommand(() => _main.Ui.OpenFolder(AppSettings.DataFolder));
            ClearActivityCommand = new AsyncCommand(ClearActivityAsync);
            ExportActivityCommand = new RelayCommand(ExportActivity);
            BackupCommand = new RelayCommand(Backup);
            Themes = new ObservableCollection<ThemeOption>(ThemeCatalog.Available.Select(t => new ThemeOption(t)));
            SelectThemeCommand = new RelayCommand(p =>
            {
                var option = p as ThemeOption;
                if (option != null) _main.Theme = option.Key;
                SyncThemes();
            });
            SyncThemes();
        }

        public ObservableCollection<ThemeOption> Themes { get; }
        public RelayCommand SelectThemeCommand { get; }

        private void SyncThemes()
        {
            foreach (var t in Themes) t.IsSelected = t.Key == _main.Theme;
            Raise(nameof(WalletIconName));
        }

        public string WalletIconName => _main.WalletIconName;

        public RelayCommand OpenAlternativeCommand { get; }
        public RelayCommand CopyAddressesCommand { get; }
        public RelayCommand ClearCacheCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }
        public AsyncCommand ClearActivityCommand { get; }
        public RelayCommand ExportActivityCommand { get; }
        public RelayCommand BackupCommand { get; }

        public static string ToCsv(IEnumerable<HistoryEntry> entries)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Date,Network,Coin,Amount,Fee,Fee coin,To,Transaction id,Explorer");
            foreach (var e in entries)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    e.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    e.Network, e.Symbol, e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    e.Fee.ToString(System.Globalization.CultureInfo.InvariantCulture), e.FeeSymbol, e.To, e.TxId, e.ExplorerUrl,
                }.Select(Csv)));
            }
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0 && !char.IsDigit(value.Length > 1 ? value[1] : 'x')) value = "'" + value;
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private void ExportActivity()
        {
            var mine = new HashSet<string>(_main.Wallet.Chains.Select(c => c.Address).Where(a => a != null));
            var entries = _main.History.Entries.Where(e => mine.Contains(e.OwnAddress)).ToList();
            if (entries.Count == 0)
            {
                _main.ShowToast(Loc.T("There is no activity to export yet"), true);
                return;
            }
            var path = _main.Ui.PickSavePath("sillet-activity-" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".csv", "CSV file|*.csv");
            if (path == null) return;
            try
            {
                System.IO.File.WriteAllText(path, ToCsv(entries), new System.Text.UTF8Encoding(true));
                _main.ShowToast(Loc.T("Activity exported"));
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                _main.ShowToast(Loc.T("Could not save the file"), true);
            }
        }

        public ObservableCollection<ProfileRow> Wallets { get; }

        public string HeroName => _main.ActiveProfileName;
        public string HeroDetail
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(_main.ActiveAccountName)) parts.Add(_main.ActiveAccountName);
                parts.Add(Wallets.Count == 1 ? Loc.T("1 wallet") : Loc.F("{0} wallets", Wallets.Count));
                parts.Add(_main.Currency);
                return string.Join("  \u00B7  ", parts);
            }
        }
        public string HeroIconName => Lookup.WalletIcon;
        public bool IsSeedWallet => !_main.Wallet.IsWatchOnly;
        public bool IsWatchWallet => _main.Wallet.IsWatchOnly;
        public RelayCommand AddWalletCommand => _main.AddWalletCommand;
        public RelayCommand AddWatchWalletCommand => _main.AddWatchWalletCommand;

        private string _proxyText;
        private string _endpointsText;
        private string _networkMessage;
        private bool _networkIsError;

        public RelayCommand SaveNetworkCommand { get; }
        public RelayCommand UseTorCommand { get; }

        public string ProxyText
        {
            get { return _proxyText; }
            set { if (Set(ref _proxyText, value ?? "")) NetworkMessage = null; }
        }

        public string EndpointsText
        {
            get { return _endpointsText; }
            set { if (Set(ref _endpointsText, value ?? "")) NetworkMessage = null; }
        }

        public string NetworkMessage
        {
            get { return _networkMessage; }
            private set
            {
                if (Set(ref _networkMessage, value)) Raise(nameof(HasNetworkMessage));
            }
        }

        public bool HasNetworkMessage => !string.IsNullOrEmpty(_networkMessage);
        public bool NetworkIsError => _networkIsError;
        public string EndpointIds => string.Join(", ", Sillet.Net.Endpoints.Supported);

        private void SaveNetwork()
        {
            var proxy = (_proxyText ?? "").Trim();
            var proxyProblem = Sillet.Net.Http.ValidateProxy(proxy);
            if (proxyProblem != null)
            {
                _networkIsError = true;
                Raise(nameof(NetworkIsError));
                NetworkMessage = Loc.T("The proxy must look like socks5://127.0.0.1:9050");
                return;
            }
            var bad = Sillet.Net.Endpoints.Validate(_endpointsText);
            if (bad != null)
            {
                _networkIsError = true;
                Raise(nameof(NetworkIsError));
                NetworkMessage = Loc.F("This line is not valid: {0}", bad);
                return;
            }
            _main.Settings.ProxyUrl = proxy;
            _main.Settings.CustomEndpoints = (_endpointsText ?? "").Trim();
            _main.Settings.Save();
            _main.Settings.ApplyNetwork();
            _networkIsError = false;
            Raise(nameof(NetworkIsError));
            NetworkMessage = Loc.T("Saved. Restart Sillet so every connection uses it");
        }

        private IChain _tokenNetwork;
        private Asset _foundToken;
        private string _tokenContract = "";
        private string _tokenError;
        private bool _tokenBusy;

        public ObservableCollection<TokenRow> TokenRows { get; }
        public AsyncCommand LookupTokenCommand { get; }
        public RelayCommand AddTokenCommand { get; }
        public IReadOnlyList<IChain> TokenNetworks => _main.Wallet.Chains.Where(c => c.SupportsCustomTokens).ToList();
        public bool HasTokens => TokenRows.Count > 0;

        public IChain SelectedTokenNetwork
        {
            get { return _tokenNetwork ?? (_tokenNetwork = TokenNetworks.FirstOrDefault()); }
            set
            {
                if (Set(ref _tokenNetwork, value)) ClearFoundToken();
            }
        }

        public string TokenContract
        {
            get { return _tokenContract; }
            set
            {
                if (Set(ref _tokenContract, value ?? "")) ClearFoundToken();
            }
        }

        public string TokenError
        {
            get { return _tokenError; }
            private set
            {
                if (Set(ref _tokenError, value)) Raise(nameof(HasTokenError));
            }
        }

        public bool HasTokenError => !string.IsNullOrEmpty(_tokenError);
        public bool TokenBusy => _tokenBusy;
        public bool HasFoundToken => _foundToken != null;
        public string FoundTokenText => _foundToken == null ? "" : Loc.F("{0} ({1}), {2} decimals", _foundToken.Symbol, _foundToken.Name, _foundToken.Decimals);
        public bool CanAddToken => _main.Tokens != null;

        private void ClearFoundToken()
        {
            _foundToken = null;
            TokenError = null;
            Raise(nameof(HasFoundToken), nameof(FoundTokenText));
        }

        public void ReloadTokens()
        {
            TokenRows.Clear();
            if (_main.Tokens == null) return;
            foreach (var chain in _main.Wallet.Chains)
                foreach (var asset in chain.Assets.Where(a => a.IsCustom)) TokenRows.Add(new TokenRow(_main, chain, asset));
            Raise(nameof(HasTokens));
        }

        private async Task LookupTokenAsync()
        {
            var chain = SelectedTokenNetwork;
            if (chain == null) return;
            if (string.IsNullOrWhiteSpace(_tokenContract))
            {
                TokenError = Loc.T("Paste the token's contract address");
                return;
            }
            _tokenBusy = true;
            Raise(nameof(TokenBusy));
            try
            {
                var asset = await chain.LookupTokenAsync(_tokenContract.Trim());
                _foundToken = asset;
                TokenError = null;
            }
            catch (WalletException e)
            {
                _foundToken = null;
                TokenError = MainViewModel.Clean(e.Message);
            }
            catch (System.Exception)
            {
                _foundToken = null;
                TokenError = Loc.T("No connection to the network");
            }
            finally
            {
                _tokenBusy = false;
                Raise(nameof(TokenBusy), nameof(HasFoundToken), nameof(FoundTokenText));
            }
        }

        private void AddFoundToken()
        {
            if (_foundToken == null || SelectedTokenNetwork == null) return;
            try
            {
                _main.AddCustomToken(SelectedTokenNetwork, _foundToken);
            }
            catch (WalletException e)
            {
                TokenError = MainViewModel.Clean(e.Message);
            }
        }

        public ObservableCollection<AccountRow> AccountRows { get; }
        public RelayCommand AddAccountCommand { get; }
        public bool CanAddAccount => _main.Accounts != null && _main.Accounts.CanAdd;

        public void ReloadAccounts()
        {
            AccountRows.Clear();
            if (_main.Accounts == null || _main.Wallet.IsWatchOnly) return;
            foreach (var i in _main.Accounts.Indexes) AccountRows.Add(new AccountRow(_main, this, i));
            Raise(nameof(CanAddAccount));
        }

        private void AddAccount()
        {
            if (_main.Accounts == null || !_main.Accounts.CanAdd) return;
            var index = _main.Accounts.Add("");
            if (index < 0) return;
            ReloadAccounts();
            _main.ShowToast(Loc.F("{0} added. Switch to it to use it", _main.Accounts.Name(index)));
        }

        public void ReloadWallets()
        {
            Wallets.Clear();
            foreach (var p in _main.Profiles.Items) Wallets.Add(new ProfileRow(_main, this, p));
        }

        internal void ExportProfile(Sillet.Storage.Profile profile)
        {
            var path = _main.Ui.PickSavePath("sillet-" + Safe(profile.Name) + "-backup-" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".json", "Sillet backup|*.json");
            if (path == null) return;
            try
            {
                System.IO.File.WriteAllText(path, Sillet.Storage.Vault.ReadInner(_main.Profiles.VaultPath(profile)));
                _main.ShowToast(Loc.F("Encrypted backup of {0} saved", profile.Name));
            }
            catch (WalletException e)
            {
                _main.ShowToast(MainViewModel.Clean(e.Message), true);
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                _main.ShowToast(Loc.T("Could not save the backup"), true);
            }
        }

        internal void ExportWatch(Sillet.Storage.Profile profile)
        {
            if (profile == null || !profile.HasAddresses) return;
            var path = _main.Ui.PickSavePath("sillet-" + Safe(profile.Name) + "-addresses.json", "Sillet watch-only file|*.json");
            if (path == null) return;
            try
            {
                System.IO.File.WriteAllText(path, Sillet.Storage.WatchFile.Serialize(profile.Name, profile.Watch));
                _main.ShowToast(Loc.F("Addresses of {0} saved. Open them with Watch a hardware wallet", profile.Name));
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                _main.ShowToast(Loc.T("Could not save the file"), true);
            }
        }

        internal async Task RemoveWatchAsync(Sillet.Storage.Profile profile)
        {
            if (!await _main.ConfirmAsync(Loc.F("Remove {0}", profile.Name), Loc.T("Only the saved addresses are removed. Your funds stay on the hardware wallet"), Loc.T("Remove"), true)) return;
            var active = _main.ActiveProfile != null && _main.ActiveProfile.Id == profile.Id;
            _main.RemoveWatchProfile(profile);
            if (!active) ReloadWallets();
            _main.ShowToast(Loc.F("{0} removed", profile.Name));
        }

        private static string Safe(string name)
        {
            var chars = (name ?? "wallet").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            return new string(chars).Trim('-');
        }

        private void Backup()
        {
            var path = _main.Ui.PickSavePath("sillet-backup-" + System.DateTime.Now.ToString("yyyy-MM-dd") + ".json", "Sillet backup|*.json");
            if (path == null) return;
            try
            {
                _main.Wallet.ExportBackup(path);
                _main.ShowToast(Loc.T("Encrypted backup saved. It opens with your current password"));
            }
            catch (WalletException e)
            {
                _main.ShowToast(e.Message, true);
            }
            catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
            {
                _main.ShowToast(Loc.T("Could not save the backup"), true);
            }
        }
        public AsyncCommand ChangePasswordCommand { get; }

        private bool Flag(System.Func<AppSettings, bool> get) { return get(_main.Settings); }

        private void Save(System.Action<AppSettings> change, params string[] names)
        {
            change(_main.Settings);
            _main.Settings.Save();
            Raise(names);
            RaiseSecurity();
        }

        private void RaiseSecurity()
        {
            Raise(nameof(SecurityOn), nameof(SecurityTotal), nameof(SecurityScoreText), nameof(SecurityScore), nameof(SecurityTip), nameof(SecurityLevelText));
        }

        private IEnumerable<KeyValuePair<string, bool>> Protections()
        {
            var s = _main.Settings;
            yield return new KeyValuePair<string, bool>(Loc.T("Lock the wallet file to this computer"), _main.Settings.LockVaultToDevice);
            yield return new KeyValuePair<string, bool>(Loc.T("Harden the Sillet process"), _main.Settings.HardenProcess);
            yield return new KeyValuePair<string, bool>(Loc.T("Set up Windows Hello"), _main.Passkey.IsSet);
            yield return new KeyValuePair<string, bool>(Loc.T("Turn on auto-lock"), _main.AutoLockMinutes > 0);
            yield return new KeyValuePair<string, bool>(Loc.T("Lock Sillet when Windows locks or sleeps"), s.LockOnSystemLock);
            yield return new KeyValuePair<string, bool>(Loc.T("Slow down wrong passwords"), s.SlowDownUnlock);
            yield return new KeyValuePair<string, bool>(Loc.T("Turn on clipboard protection"), s.ClipboardGuard);
            yield return new KeyValuePair<string, bool>(Loc.T("Warn on new addresses"), s.WarnNewAddress);
            yield return new KeyValuePair<string, bool>(Loc.T("Catch lookalike addresses"), s.WarnLookalike);
            yield return new KeyValuePair<string, bool>(Loc.T("Require your password to send"), s.RequirePasswordToSend);
            yield return new KeyValuePair<string, bool>(Loc.T("Add an extra check for large sends"), s.LargeSendLevel > 0);
            yield return new KeyValuePair<string, bool>(Loc.T("Hide Sillet from screenshots and screen sharing"), s.BlockScreenCapture);
            yield return new KeyValuePair<string, bool>(Loc.T("Blur Sillet when you switch to another window"), s.BlurWhenInactive);
            yield return new KeyValuePair<string, bool>(Loc.T("Auto-clear copied addresses"), s.AutoClearClipboard);
            yield return new KeyValuePair<string, bool>(Loc.T("Lock when minimized"), s.LockOnMinimize);
            yield return new KeyValuePair<string, bool>(Loc.T("Only send to saved contacts"), s.ContactsOnly);
        }

        public int SecurityOn => Protections().Count(p => p.Value);
        public int SecurityTotal => Protections().Count();
        public double SecurityScore => SecurityTotal == 0 ? 0 : (double)SecurityOn / SecurityTotal;
        public string SecurityScoreText => Loc.F("{0} of {1} protections on", SecurityOn, SecurityTotal);

        public string SecurityLevelText
        {
            get
            {
                var score = SecurityScore;
                if (score >= 0.75) return Loc.T("Strong");
                if (score >= 0.45) return Loc.T("Good");
                return Loc.T("Basic");
            }
        }

        public string SecurityTip
        {
            get
            {
                var next = Protections().FirstOrDefault(p => !p.Value);
                return next.Key == null ? Loc.T("Everything is on. Nice") : Loc.T("Next step: ") + next.Key.Substring(0, 1).ToLower(Loc.Instance.Culture) + next.Key.Substring(1);
            }
        }

        public RelayCommand TestPushCommand { get; }

        public bool SoundsOn
        {
            get { return Flag(s => s.SoundsOn); }
            set
            {
                Save(s => s.SoundsOn = value, nameof(SoundsOn));
                if (value) Sounds.Play(Sounds.Favorite);
            }
        }

        public bool NotificationsOn
        {
            get { return Flag(s => s.NotificationsOn); }
            set { Save(s => s.NotificationsOn = value, nameof(NotificationsOn)); }
        }

        public bool NotifyPayments
        {
            get { return Flag(s => s.NotifyPayments); }
            set { Save(s => s.NotifyPayments = value, nameof(NotifyPayments)); }
        }

        public bool NotifyNews
        {
            get { return Flag(s => s.NotifyNews); }
            set { Save(s => s.NotifyNews = value, nameof(NotifyNews)); }
        }

        public bool NotifyBitcoin
        {
            get { return Flag(s => s.NotifyBitcoin); }
            set
            {
                if (value && !_main.Settings.NotifyBitcoin) _main.Notifications.ResetBitcoin();
                Save(s => s.NotifyBitcoin = value, nameof(NotifyBitcoin));
                if (value) _main.CheckBitcoin();
            }
        }

        public bool BitcoinMove3
        {
            get { return _main.Settings.BitcoinMovePercent == 3; }
            set { if (value) SetBitcoinMove(3); }
        }

        public bool BitcoinMove5
        {
            get { return _main.Settings.BitcoinMovePercent == 5; }
            set { if (value) SetBitcoinMove(5); }
        }

        public bool BitcoinMove10
        {
            get { return _main.Settings.BitcoinMovePercent == 10; }
            set { if (value) SetBitcoinMove(10); }
        }

        private void SetBitcoinMove(int percent)
        {
            Save(s => s.BitcoinMovePercent = percent, nameof(BitcoinMove3), nameof(BitcoinMove5), nameof(BitcoinMove10));
        }

        public bool PushNotifications
        {
            get { return Flag(s => s.PushNotifications); }
            set { Save(s => s.PushNotifications = value, nameof(PushNotifications)); }
        }

        public bool RunInBackground
        {
            get { return Flag(s => s.RunInBackground); }
            set
            {
                Save(s =>
                {
                    s.RunInBackground = value;
                    s.StartWithSystem = value;
                }, nameof(RunInBackground));
                _main.Ui.ApplyBackground(value);
            }
        }

        public bool LockVaultToDevice
        {
            get { return Flag(s => s.LockVaultToDevice); }
            set
            {
                Save(s => s.LockVaultToDevice = value, nameof(LockVaultToDevice), nameof(VaultLockText));
                Sillet.Storage.Vault.LockToDevice = value;
                try
                {
                    _main.Wallet.ApplyFileProtection();
                    _main.ShowToast(value ? Loc.T("Wallet file locked to this computer") : Loc.T("Wallet file can be moved to another computer"));
                }
                catch (System.Exception e) when (e is WalletException || e is System.IO.IOException || e is System.UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException)
                {
                    _main.ShowToast(MainViewModel.Friendly(e), true);
                }
                Raise(nameof(VaultLockText));
            }
        }

        public string VaultLockText => _main.Wallet.IsFileLockedToDevice
            ? Loc.T("On. A stolen copy of the wallet file can't be opened on another PC or Windows user")
            : Loc.T("A copied wallet file could be attacked on another PC");

        public bool HardenProcess
        {
            get { return Flag(s => s.HardenProcess); }
            set
            {
                Save(s => s.HardenProcess = value, nameof(HardenProcess), nameof(HardenText));
                _main.ShowToast(Loc.T("Restart Sillet to apply"));
            }
        }

        public string HardenText
        {
            get
            {
                var active = _main.Ui.IsProcessHardened();
                if (active && _main.Settings.HardenProcess) return Loc.T("Active. Other apps can't read Sillet's memory or inject code into it");
                if (active) return Loc.T("Still active until you restart Sillet");
                if (_main.Settings.HardenProcess) return Loc.T("Turns on the next time Sillet starts");
                return Loc.T("Blocks other apps from reading Sillet's memory or injecting code");
            }
        }

        public bool LockOnMinimize
        {
            get { return Flag(s => s.LockOnMinimize); }
            set { Save(s => s.LockOnMinimize = value, nameof(LockOnMinimize)); }
        }

        public bool LockOnSystemLock
        {
            get { return Flag(s => s.LockOnSystemLock); }
            set { Save(s => s.LockOnSystemLock = value, nameof(LockOnSystemLock)); }
        }

        public bool BlockScreenCapture
        {
            get { return Flag(s => s.BlockScreenCapture); }
            set
            {
                Save(s => s.BlockScreenCapture = value, nameof(BlockScreenCapture));
                _main.Ui.SetCaptureProtection(value);
            }
        }

        public bool ScreenshotReminder
        {
            get { return Flag(s => s.ScreenshotReminder); }
            set { Save(s => s.ScreenshotReminder = value, nameof(ScreenshotReminder)); }
        }

        public bool BlurWhenInactive
        {
            get { return Flag(s => s.BlurWhenInactive); }
            set { Save(s => s.BlurWhenInactive = value, nameof(BlurWhenInactive)); }
        }

        public bool ContactsOnly
        {
            get { return Flag(s => s.ContactsOnly); }
            set { Save(s => s.ContactsOnly = value, nameof(ContactsOnly)); }
        }

        public bool RotateReceive
        {
            get { return Flag(s => s.RotateReceive); }
            set
            {
                Save(s => s.RotateReceive = value, nameof(RotateReceive));
                var ignore = _main.RefreshAllAsync();
            }
        }

        public bool WarnLookalike
        {
            get { return Flag(s => s.WarnLookalike); }
            set { Save(s => s.WarnLookalike = value, nameof(WarnLookalike)); }
        }

        public bool WarnNewAddress
        {
            get { return Flag(s => s.WarnNewAddress); }
            set { Save(s => s.WarnNewAddress = value, nameof(WarnNewAddress)); }
        }

        public bool SlowDownUnlock
        {
            get { return Flag(s => s.SlowDownUnlock); }
            set { Save(s => s.SlowDownUnlock = value, nameof(SlowDownUnlock)); }
        }

        public bool LargeSendOff
        {
            get { return _main.Settings.LargeSendLevel == 0; }
            set { if (value) Save(s => s.LargeSendLevel = 0, nameof(LargeSendOff), nameof(LargeSendLow), nameof(LargeSendHigh)); }
        }

        public bool LargeSendLow
        {
            get { return _main.Settings.LargeSendLevel == 1; }
            set { if (value) Save(s => s.LargeSendLevel = 1, nameof(LargeSendOff), nameof(LargeSendLow), nameof(LargeSendHigh)); }
        }

        public bool LargeSendHigh
        {
            get { return _main.Settings.LargeSendLevel == 2; }
            set { if (value) Save(s => s.LargeSendLevel = 2, nameof(LargeSendOff), nameof(LargeSendLow), nameof(LargeSendHigh)); }
        }

        public string LargeSendLowText => LimitText(1);
        public string LargeSendHighText => LimitText(2);

        private string LimitText(int level)
        {
            return Formatters.Fiat(AppSettings.LargeSendLimit(level, _main.PerUsd), _main.Currency);
        }

        public string DataSummary
        {
            get
            {
                var parts = new List<string>
                {
                    Count(_main.History.Entries.Count, "transfer"),
                    Count(_main.Contacts.Items.Count, "contact"),
                    Count(_main.Alerts.Items.Count, "alert"),
                    Count(_main.Swaps.Items.Count, "swap"),
                };
                return string.Join("  ·  ", parts);
            }
        }

        public string DataSizeText
        {
            get
            {
                long bytes = 0;
                try
                {
                    if (System.IO.Directory.Exists(AppSettings.DataFolder))
                        foreach (var f in System.IO.Directory.GetFiles(AppSettings.DataFolder, "*", System.IO.SearchOption.AllDirectories))
                        {
                            try { bytes += new System.IO.FileInfo(f).Length; }
                            catch (System.IO.IOException) { }
                        }
                }
                catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
                {
                }
                if (bytes < 1024) return Loc.F("{0} B on disk", bytes);
                if (bytes < 1024 * 1024) return Loc.F("{0} KB on disk", (bytes / 1024m).ToString("N0", Formatters.Display));
                return Loc.F("{0} MB on disk", (bytes / 1048576m).ToString("N1", Formatters.Display));
            }
        }

        public string DataFolderText => AppSettings.DataFolder;

        private static string Count(int n, string word)
        {
            return n == 1 ? "1 " + word : n + " " + word + "s";
        }

        public LanguageOption[] Languages => Loc.Languages;

        public LanguageOption SelectedLanguage
        {
            get { return Loc.Languages.FirstOrDefault(l => l.Code == Loc.Instance.Current) ?? Loc.Languages[0]; }
            set
            {
                if (value == null || value.Code == Loc.Instance.Current) return;
                _main.Settings.Language = value.Code;
                _main.Settings.Save();
                Loc.Instance.Current = value.Code;
            }
        }

        public bool StartHome
        {
            get { return _main.Settings.StartPage != "market"; }
            set { if (value) Save(s => s.StartPage = "home", nameof(StartHome), nameof(StartMarket)); }
        }

        public bool StartMarket
        {
            get { return _main.Settings.StartPage == "market"; }
            set { if (value) Save(s => s.StartPage = "market", nameof(StartHome), nameof(StartMarket)); }
        }

        public bool DefaultFeeSlow
        {
            get { return _main.Settings.DefaultFeeSpeed == "slow"; }
            set { if (value) Save(s => s.DefaultFeeSpeed = "slow", nameof(DefaultFeeSlow), nameof(DefaultFeeNormal), nameof(DefaultFeeFast)); }
        }

        public bool DefaultFeeNormal
        {
            get { return _main.Settings.DefaultFeeSpeed != "slow" && _main.Settings.DefaultFeeSpeed != "fast"; }
            set { if (value) Save(s => s.DefaultFeeSpeed = "normal", nameof(DefaultFeeSlow), nameof(DefaultFeeNormal), nameof(DefaultFeeFast)); }
        }

        public bool DefaultFeeFast
        {
            get { return _main.Settings.DefaultFeeSpeed == "fast"; }
            set { if (value) Save(s => s.DefaultFeeSpeed = "fast", nameof(DefaultFeeSlow), nameof(DefaultFeeNormal), nameof(DefaultFeeFast)); }
        }

        public bool RequirePasswordToSend
        {
            get { return Flag(s => s.RequirePasswordToSend); }
            set { Save(s => s.RequirePasswordToSend = value, nameof(RequirePasswordToSend)); }
        }

        public bool ClipboardGuard
        {
            get { return Flag(s => s.ClipboardGuard); }
            set
            {
                Save(s => s.ClipboardGuard = value, nameof(ClipboardGuard));
                _main.Guard.Enabled = value;
            }
        }

        public bool AutoClearClipboard
        {
            get { return Flag(s => s.AutoClearClipboard); }
            set { Save(s => s.AutoClearClipboard = value, nameof(AutoClearClipboard)); }
        }


        public bool HideSmallBalances
        {
            get { return _main.HideSmallBalances; }
            set
            {
                _main.HideSmallBalances = value;
                Raise(nameof(HideSmallBalances));
            }
        }

        public bool Refresh1
        {
            get { return _main.RefreshMinutes == 1; }
            set { if (value) SetRefresh(1); }
        }

        public bool Refresh2
        {
            get { return _main.RefreshMinutes == 2; }
            set { if (value) SetRefresh(2); }
        }

        public bool Refresh5
        {
            get { return _main.RefreshMinutes == 5; }
            set { if (value) SetRefresh(5); }
        }

        private void SetRefresh(int minutes)
        {
            _main.RefreshMinutes = minutes;
            Raise(nameof(Refresh1), nameof(Refresh2), nameof(Refresh5));
        }

        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public int PasswordFieldsVersion { get; private set; }

        private string _passkeyError;
        private bool _passkeyBusy;
        private string _passkeyUnavailable;
        private bool _passkeyChecked;

        public AsyncCommand SetupPasskeyCommand { get; }
        public RelayCommand RemovePasskeyCommand { get; }
        public string PasskeyPassword { get; set; }
        public int PasskeyFieldsVersion { get; private set; }
        public bool HasPasskey => _main.Passkey.IsSet;

        public string PasskeyUnavailableText
        {
            get
            {
                if (!_passkeyChecked)
                {
                    _passkeyChecked = true;
                    _passkeyUnavailable = _main.Ui.PasskeyUnavailable();
                }
                return _passkeyUnavailable ?? "";
            }
        }

        public bool PasskeyAvailable => PasskeyUnavailableText.Length == 0;
        public bool CanSetupPasskey => !HasPasskey && PasskeyAvailable;

        public string PasskeyStatusText => HasPasskey
            ? Loc.T("On. Unlock and confirm sends with your face, fingerprint or PIN")
            : Loc.T("Unlock with your face, fingerprint or PIN instead of typing the password");

        public bool IsPasskeyBusy
        {
            get { return _passkeyBusy; }
            private set { Set(ref _passkeyBusy, value); }
        }

        public string PasskeyError
        {
            get { return _passkeyError; }
            private set
            {
                Set(ref _passkeyError, value);
                Raise(nameof(HasPasskeyError));
            }
        }

        public bool HasPasskeyError => !string.IsNullOrEmpty(PasskeyError);

        private void RaisePasskey()
        {
            Raise(nameof(HasPasskey), nameof(CanSetupPasskey), nameof(PasskeyStatusText));
            RaiseSecurity();
        }

        private async Task SetupPasskeyAsync()
        {
            PasskeyError = null;
            var password = PasskeyPassword ?? "";
            if (password.Length == 0)
            {
                PasskeyError = Loc.T("Type your password first");
                return;
            }
            IsPasskeyBusy = true;
            try
            {
                await Task.Run(() => _main.Wallet.RevealMnemonic(password));
                await _main.Passkey.SetupAsync(_main.Ui, password);
                PasskeyPassword = "";
                PasskeyFieldsVersion++;
                Raise(nameof(PasskeyFieldsVersion));
                RaisePasskey();
                _main.ShowToast(Loc.T("Windows Hello is ready"));
            }
            catch (WalletException e)
            {
                PasskeyError = MainViewModel.Clean(e.Message);
            }
            catch (PasskeyException e)
            {
                if (!e.Cancelled) PasskeyError = Loc.T(e.Message);
            }
            finally
            {
                IsPasskeyBusy = false;
            }
        }

        private void RemovePasskey()
        {
            _main.Passkey.Remove(_main.Ui);
            PasskeyError = null;
            RaisePasskey();
            _main.ShowToast(Loc.T("Windows Hello removed"));
        }

        public string PasswordError
        {
            get { return _passwordError; }
            private set
            {
                Set(ref _passwordError, value);
                Raise(nameof(HasPasswordError));
            }
        }

        public bool HasPasswordError => PasswordError != null;

        private async Task ChangePasswordAsync()
        {
            PasswordError = null;
            var current = CurrentPassword;
            var next = NewPassword;
            if (current.Length == 0)
            {
                PasswordError = Loc.T("Type your current password");
                return;
            }
            if (next.Length >= 8 && !PasswordStrength.IsAcceptable(next))
            {
                PasswordError = PasswordStrength.Advice;
                return;
            }
            try
            {
                await Task.Run(() => _main.Wallet.ChangePassword(current, next));
                CurrentPassword = "";
                NewPassword = "";
                PasswordFieldsVersion++;
                Raise(nameof(PasswordFieldsVersion));
                if (_main.Passkey.IsSet)
                {
                    _main.Passkey.Remove(_main.Ui);
                    RaisePasskey();
                    _main.ShowToast(Loc.T("Password changed. Set up Windows Hello again"));
                }
                else _main.ShowToast(Loc.T("Password changed"));
            }
            catch (WalletException e)
            {
                PasswordError = MainViewModel.Clean(e.Message);
            }
        }

        private void ClearCache()
        {
            int removed = 0;
            foreach (var folder in new[] { "cache", "icons" })
            {
                var path = System.IO.Path.Combine(AppSettings.DataFolder, folder);
                try
                {
                    if (!System.IO.Directory.Exists(path)) continue;
                    foreach (var file in System.IO.Directory.GetFiles(path))
                    {
                        try
                        {
                            System.IO.File.Delete(file);
                            removed++;
                        }
                        catch (System.IO.IOException)
                        {
                        }
                    }
                }
                catch (System.UnauthorizedAccessException)
                {
                }
            }
            _main.ShowToast(removed == 1 ? Loc.T("1 cached file removed") : Loc.F("{0} cached files removed", removed));
            Raise(nameof(DataSizeText));
        }

        private async Task ClearActivityAsync()
        {
            if (!await _main.ConfirmAsync(Loc.T("Clear activity"), Loc.T("Removes the local list of transfers sent from this computer. Your funds are not affected"), Loc.T("Clear"), true))
                return;
            _main.History.RemoveFrom(_main.Wallet.Chains.Select(c => c.Address).Where(a => a != null).ToList());
            if (_main.Activity != null) _main.Activity.Reload();
            _main.ShowToast(Loc.T("Activity cleared"));
            Raise(nameof(DataSummary), nameof(DataSizeText));
        }

        public bool AutoLockOff
        {
            get { return _main.AutoLockMinutes == 0; }
            set { if (value) SetAutoLock(0); }
        }

        public bool AutoLock5
        {
            get { return _main.AutoLockMinutes == 5; }
            set { if (value) SetAutoLock(5); }
        }

        public bool AutoLock15
        {
            get { return _main.AutoLockMinutes == 15; }
            set { if (value) SetAutoLock(15); }
        }

        public bool AutoLock30
        {
            get { return _main.AutoLockMinutes == 30; }
            set { if (value) SetAutoLock(30); }
        }

        private void SetAutoLock(int minutes)
        {
            _main.AutoLockMinutes = minutes;
            Raise(nameof(AutoLockOff), nameof(AutoLock5), nameof(AutoLock15), nameof(AutoLock30));
            RaiseSecurity();
        }

        private string _currencyQuery = "";
        private bool _isCurrencyPickerOpen;
        private List<CurrencyRow> _currencyRows;
        private RelayCommand _pickCurrencyCommand;
        private RelayCommand _toggleCurrencyPickerCommand;

        public RelayCommand PickCurrencyCommand
        {
            get
            {
                return _pickCurrencyCommand ?? (_pickCurrencyCommand = new RelayCommand(p =>
                {
                    var row = p as CurrencyRow;
                    if (row == null) return;
                    SelectedCurrency = row.Info;
                    IsCurrencyPickerOpen = false;
                }));
            }
        }

        public RelayCommand ToggleCurrencyPickerCommand
        {
            get { return _toggleCurrencyPickerCommand ?? (_toggleCurrencyPickerCommand = new RelayCommand(() => IsCurrencyPickerOpen = !IsCurrencyPickerOpen)); }
        }

        public bool IsCurrencyPickerOpen
        {
            get { return _isCurrencyPickerOpen; }
            set
            {
                if (!Set(ref _isCurrencyPickerOpen, value)) return;
                if (value)
                {
                    CurrencyQuery = "";
                    _currencyRows = null;
                    var ignored = RefreshFlagsAsync();
                }
                Raise(nameof(CurrencyResults));
            }
        }

        private async Task RefreshFlagsAsync()
        {
            if (!await Flags.EnsureAsync()) return;
            _currencyRows = null;
            Raise(nameof(CurrencyResults));
        }

        public string CurrencyQuery
        {
            get { return _currencyQuery; }
            set
            {
                if (!Set(ref _currencyQuery, value ?? "")) return;
                _currencyRows = null;
                Raise(nameof(CurrencyResults), nameof(HasCurrencyResults));
            }
        }

        public IReadOnlyList<CurrencyRow> CurrencyResults
        {
            get
            {
                if (_currencyRows == null)
                    _currencyRows = Currencies.Search(_currencyQuery, _main.LocalCurrency).Select(c => new CurrencyRow(c, c.Code == _main.Currency)).ToList();
                return _currencyRows;
            }
        }

        public bool HasCurrencyResults => CurrencyResults.Count > 0;
        public string SelectedCurrencyText => Currencies.Get(_main.Currency).Display;

        public CurrencyInfo SelectedCurrency
        {
            get { return Currencies.Get(_main.Currency); }
            set
            {
                if (value == null || value.Code == _main.Currency) return;
                _main.Currency = value.Code;
                _currencyRows = null;
                Raise(nameof(SelectedCurrency), nameof(SelectedCurrencyText), nameof(CurrencyResults), nameof(LargeSendLowText), nameof(LargeSendHighText), nameof(SmallBalanceText));
            }
        }

        public string SmallBalanceText => Loc.F("Under {0}", Formatters.Fiat(_main.UsdToFiat(1m), _main.Currency));

        public bool HideBalances
        {
            get { return _main.HideBalances; }
            set
            {
                _main.HideBalances = value;
                Raise(nameof(HideBalances));
            }
        }

        public string RevealPassword { get; set; }
        public ObservableCollection<WordItem> Words { get; }

        public bool IsRevealed
        {
            get { return _isRevealed; }
            private set { Set(ref _isRevealed, value); }
        }

        public string RevealError
        {
            get { return _revealError; }
            private set
            {
                Set(ref _revealError, value);
                Raise(nameof(HasRevealError));
            }
        }

        public bool HasRevealError => RevealError != null;
        public AsyncCommand RevealCommand { get; }
        public RelayCommand HideWordsCommand { get; }

        public string DeletePassword { get; set; }

        public string DeleteError
        {
            get { return _deleteError; }
            private set
            {
                Set(ref _deleteError, value);
                Raise(nameof(HasDeleteError));
            }
        }

        public bool HasDeleteError => DeleteError != null;
        public AsyncCommand DeleteCommand { get; }

        public RelayCommand OpenCoinGeckoCommand { get; }
        public RelayCommand OpenRatonCommand => _main.OpenRatonCommand;
        public string Version => "release " + AppInfo.CurrentVersion;

        // ---- Developer mode: made-up payments, receipts and notifications. Nothing here signs, sends or touches a real network ----
        private const string TestMark = "[test] ";
        private bool _isDeveloperOpen;
        private string _devStatus = "";
        public RelayCommand ToggleDeveloperCommand { get; }
        public RelayCommand DevFakeSentCommand { get; }
        public AsyncCommand DevFakeReceivedCommand { get; }
        public RelayCommand DevNotificationsCommand { get; }
        public RelayCommand DevReceiptCommand { get; }
        public RelayCommand DevClearCommand { get; }
        public RelayCommand DevBigPaymentCommand { get; }
        public RelayCommand DevManyEntriesCommand { get; }

        public bool IsDeveloperOpen
        {
            get { return _isDeveloperOpen; }
            set
            {
                if (Set(ref _isDeveloperOpen, value)) DevStatus = "";
            }
        }

        public string DevStatus
        {
            get { return _devStatus; }
            private set
            {
                if (Set(ref _devStatus, value)) Raise(nameof(HasDevStatus));
            }
        }

        public bool HasDevStatus => !string.IsNullOrEmpty(_devStatus);

        private static string DevHex(int bytes)
        {
            return Guid.NewGuid().ToString("N") + (bytes > 16 ? Guid.NewGuid().ToString("N") : "");
        }

        private HistoryEntry DevEntry(bool incoming, string symbol, decimal amount)
        {
            const string me = "0x7a3F2c9B1e5D4a8C6b0E9f1D2c3B4a5E6f7A8b9C";
            const string other = "0x9D2e4F6a8B0c1E3d5F7a9B1c3D5e7F9a0B2c4D6e";
            var tx = "dev-" + DevHex(32).Substring(0, 56);
            return new HistoryEntry(DateTime.UtcNow, "ethereum", "Ethereum", symbol, amount, incoming ? 0m : 0.00021m, "ETH",
                incoming ? other : me, incoming ? me : other, tx, "https://etherscan.io/tx/" + tx, incoming);
        }

        private void DevFakeSent()
        {
            var entry = DevEntry(false, "ETH", 0.0421m);
            _main.History.Add(entry);
            if (_main.Activity != null) _main.Activity.Reload();
            _main.Navigate("send");
            DevStatus = Loc.T("Showing a test payment. It also appears in Activity");
            var ignored = ShowFakeSentLaterAsync(entry);
        }

        private async Task ShowFakeSentLaterAsync(HistoryEntry entry)
        {
            await Task.Delay(700);
            if (_main.Send != null) _main.Send.ShowFakeSent(entry.Symbol, entry.Amount, entry.Network, entry.From, entry.To, entry.TxId, entry.Fee);
        }

        private void DevBigPayment()
        {
            var entry = DevEntry(true, "BTC", 1.25m);
            _main.History.Add(entry);
            if (_main.Activity != null) _main.Activity.Reload();
            _main.Notify("payment", TxStatus.ReceivedTitle(TxStage.Final), TestMark + Loc.F("{0} arrived on {1}", "1.25 BTC", "Bitcoin") + "\n" + TxStatus.ReceivedLine(TxStage.Final, entry.From), "activity");
            DevStatus = Loc.T("Added a large test payment");
        }

        private void DevManyEntries()
        {
            for (var i = 0; i < 40; i++) _main.History.Add(DevEntry(i % 3 == 0, i % 2 == 0 ? "ETH" : "USDC", 0.01m * (i + 1)));
            if (_main.Activity != null) _main.Activity.Reload();
            DevStatus = Loc.F("Added {0} test transfers", 40);
        }

        private async Task DevFakeReceivedAsync()
        {
            var entry = DevEntry(true, "USDC", 125.5m);
            var amount = Formatters.Crypto(entry.Amount) + " " + entry.Symbol;
            DevStatus = Loc.T("Test payment on its way. Watch the notifications");
            _main.Notify("payment", TxStatus.ReceivedTitle(TxStage.Pending), TestMark + Loc.F("{0} arrived on {1}", amount, entry.Network) + "\n" + TxStatus.ReceivedLine(TxStage.Pending, entry.From), "activity");
            await Task.Delay(TimeSpan.FromSeconds(3));
            _main.History.Add(entry);
            if (_main.Activity != null) _main.Activity.Reload();
            _main.Notify("payment", TxStatus.ReceivedTitle(TxStage.Confirmed), TestMark + Loc.F("{0} arrived on {1}", amount, entry.Network) + "\n" + TxStatus.ReceivedLine(TxStage.Confirmed, entry.From), "activity");
            await Task.Delay(TimeSpan.FromSeconds(3));
            _main.Notify("payment", TxStatus.ReceivedTitle(TxStage.Final), TestMark + Loc.F("{0} arrived on {1}", amount, entry.Network) + "\n" + TxStatus.ReceivedLine(TxStage.Final, entry.From), "activity");
            DevStatus = Loc.T("Test payment done: pending, received, confirmed");
        }

        private void DevNotifications()
        {
            _main.Notify("payment", Loc.T("Payment received"), TestMark + Loc.F("{0} arrived on {1}", "0.5 ETH", "Ethereum") + "\n" + TxStatus.ReceivedLine(TxStage.Confirmed, "0x9D2e4F6a8B0c1E3d5F7a9B1c3D5e7F9a0B2c4D6e"), "activity");
            _main.Notify("news", TestMark + Loc.T("News from Raton"), Loc.T("A test announcement"), "home");
            _main.Notify("price_up", Loc.T("Bitcoin is up"), TestMark + Loc.F("Bitcoin moved {0}% to {1}", "+3.2", "$70,000"), "market");
            _main.Notify("price_down", Loc.T("Bitcoin is down"), TestMark + Loc.F("Bitcoin moved {0}% to {1}", "\u22123.2", "$64,000"), "market");
            _main.Notify("alert", Loc.T("Price alert"), TestMark + Loc.T("A test price alert"), "market");
            _main.Notify("security", Loc.T("Security"), TestMark + Loc.T("A test security notice"), "settings");
            DevStatus = _main.Settings.NotificationsOn ? Loc.T("Test notifications added") : Loc.T("Turn notifications on first");
        }

        private void DevReceipt()
        {
            _main.History.Add(DevEntry(false, "ETH", 0.0189m));
            _main.History.Add(DevEntry(true, "USDT", 250m));
            if (_main.Activity == null) return;
            _main.Activity.Reload();
            _main.Navigate("activity");
            var item = _main.Activity.Items.FirstOrDefault(i => i.Entry.TxId.StartsWith("dev-", StringComparison.Ordinal));
            if (item != null) _main.Activity.OpenReceiptCommand.Execute(item);
            DevStatus = Loc.T("Opened a test receipt");
        }

        private void DevClear()
        {
            var history = _main.History.RemoveWhere(e => e.TxId != null && e.TxId.StartsWith("dev-", StringComparison.Ordinal));
            var notes = _main.Notifications.RemoveWhere(n => n.Message != null && n.Message.StartsWith(TestMark, StringComparison.Ordinal) || n.Title != null && n.Title.StartsWith(TestMark, StringComparison.Ordinal));
            if (_main.Activity != null) _main.Activity.Reload();
            _main.RefreshNotificationList();
            DevStatus = Loc.F("Removed {0} test items", history + notes);
        }

        public bool AutoUpdate
        {
            get { return Flag(s => s.AutoUpdate); }
            set
            {
                Save(s => s.AutoUpdate = value, nameof(AutoUpdate));
                if (value && CanAutoUpdate) { var ignored = CheckUpdateNowAsync(); }
                else if (!value) UpdateStatus = "";
            }
        }

        /// <summary>Only Windows builds that can write next to Sillet.exe can replace themselves.</summary>
        private bool? _canAutoUpdate;
        public bool CanAutoUpdate => (_canAutoUpdate ?? (_canAutoUpdate = UpdateService.CanSelfUpdate)).Value;
        public bool ShowCheckUpdate => !CanAutoUpdate;
        public AsyncCommand CheckUpdateCommand { get; private set; }
        public RelayCommand RestartCommand { get; private set; }
        private string _updateStatus = "";

        public string UpdateStatus
        {
            get { return _updateStatus; }
            private set
            {
                Set(ref _updateStatus, value);
                Raise(nameof(HasUpdateStatus));
            }
        }

        public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatus);

        private async Task CheckUpdateNowAsync()
        {
            UpdateStatus = Loc.T("Checking for updates");
            var result = await _main.CheckUpdateAsync(false);
            if (result == null) UpdateStatus = Loc.T("Sillet is up to date");
            else if (result.Handled) UpdateStatus = Loc.F("Sillet {0} is ready to download", result.Update.Version);
            else
            {
                UpdateStatus = Loc.F("Sillet {0} is ready to download", result.Update.Version);
                _main.Ui.OpenUrl(result.Update.Url);
            }
        }
        public int NetworkCount => _main.Wallet.Chains.Count;
        public int AssetCount => _main.Wallet.Chains.Sum(c => c.Assets.Count);

        private async Task RevealAsync()
        {
            RevealError = null;
            try
            {
                var pw = RevealPassword;
                var phrase = await Task.Run(() => _main.Wallet.RevealMnemonic(pw));
                Words.Clear();
                var words = phrase.Split(' ');
                for (int i = 0; i < words.Length; i++) Words.Add(new WordItem(i + 1, words[i]));
                IsRevealed = true;
                _main.Settings.PhraseViewed = true;
                _main.Settings.Save();
            }
            catch (WalletException e)
            {
                RevealError = MainViewModel.Clean(e.Message);
            }
        }

        private async Task DeleteAsync()
        {
            DeleteError = null;
            if (!await _main.ConfirmAsync(Loc.T("Delete wallet"),
                Loc.T("It will be deleted from this computer. You can only recover it with your recovery phrase"), Loc.T("Delete"), true))
                return;
            try
            {
                var pw = DeletePassword;
                var addresses = _main.Wallet.Chains.Select(c => c.Address).Where(a => a != null).ToList();
                await Task.Run(() => _main.Wallet.DeleteSavedWallet(pw));
                _main.History.RemoveFrom(addresses);
                _main.Passkey.Remove(_main.Ui);
                _main.Lock();
                _main.RemoveProfileAfterDelete();
                _main.ShowStart();
                _main.ShowToast(Loc.T("Wallet deleted"));
            }
            catch (WalletException e)
            {
                DeleteError = MainViewModel.Clean(e.Message);
            }
        }
    }

    public sealed class ProfileRow : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly SettingsViewModel _settings;

        public ProfileRow(MainViewModel main, SettingsViewModel settings, Sillet.Storage.Profile profile)
        {
            _main = main;
            _settings = settings;
            Profile = profile;
            SwitchCommand = new RelayCommand(() => _main.SwitchProfile(Profile));
            ExportCommand = new RelayCommand(() => _settings.ExportProfile(Profile));
            ExportWatchCommand = new RelayCommand(() => _settings.ExportWatch(Profile));
            RemoveCommand = new AsyncCommand(() => _settings.RemoveWatchAsync(Profile));
            ChoosePhotoCommand = new AsyncCommand(async () => { await _main.ChooseProfilePhotoAsync(Profile); Raise(nameof(PhotoPath), nameof(HasPhoto)); });
            RemovePhotoCommand = new RelayCommand(() => { _main.RemoveProfilePhoto(Profile); Raise(nameof(PhotoPath), nameof(HasPhoto)); });
        }

        public AsyncCommand ChoosePhotoCommand { get; }
        public RelayCommand RemovePhotoCommand { get; }
        public void RefreshPhoto() { Raise(nameof(PhotoPath), nameof(HasPhoto)); }
        public string WalletIconName => Lookup.WalletIcon;
        public string PhotoPath => Profile.PhotoPath;
        public bool HasPhoto => Profile.HasPhoto;

        public Sillet.Storage.Profile Profile { get; }
        public RelayCommand SwitchCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand ExportWatchCommand { get; }
        public bool HasAddresses => Profile.HasAddresses;
        public AsyncCommand RemoveCommand { get; }
        public string Initial => Profile.Initial;
        public string KindText => Profile.KindText;
        public bool IsWatch => Profile.IsWatch;
        public bool IsSeed => !Profile.IsWatch;
        public bool IsActive => _main.ActiveProfile != null && _main.ActiveProfile.Id == Profile.Id;
        public bool IsOther => !IsActive;

        public string Name
        {
            get { return Profile.Name; }
            set
            {
                if (string.IsNullOrWhiteSpace(value) || value.Trim() == Profile.Name) return;
                _main.RenameProfile(Profile, value);
                Raise(nameof(Name), nameof(Initial));
            }
        }
    }

    public sealed class AccountRow : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly SettingsViewModel _settings;

        public AccountRow(MainViewModel main, SettingsViewModel settings, int index)
        {
            _main = main;
            _settings = settings;
            Index = index;
            SwitchCommand = new RelayCommand(() => _main.SwitchAccount(Index));
            ChoosePhotoCommand = new AsyncCommand(async () => { await _main.ChoosePhotoAsync(_main.AccountPhotoKey(Index)); Raise(nameof(PhotoPath), nameof(HasPhoto)); });
            RemovePhotoCommand = new RelayCommand(() => { _main.RemovePhoto(_main.AccountPhotoKey(Index)); Raise(nameof(PhotoPath), nameof(HasPhoto)); });
        }

        public string WalletIconName => Lookup.WalletIcon;
        public void RefreshPhoto() { Raise(nameof(PhotoPath), nameof(HasPhoto)); }
        public AsyncCommand ChoosePhotoCommand { get; }
        public RelayCommand RemovePhotoCommand { get; }
        public string PhotoPath => Sillet.Storage.AvatarStore.PathFor(_main.AccountPhotoKey(Index));
        public bool HasPhoto => PhotoPath != null;

        public int Index { get; }
        public RelayCommand SwitchCommand { get; }
        public bool IsActive => _main.Accounts.Active == Index;
        public bool IsOther => !IsActive;
        public string Initial => (Index + 1).ToString();
        public string Path => Index == 0 ? "" : Loc.F("Account {0}", Index + 1);

        public string Name
        {
            get { return _main.Accounts.Name(Index); }
            set
            {
                if (string.IsNullOrWhiteSpace(value) || value.Trim() == Name) return;
                _main.Accounts.Rename(Index, value);
                Raise(nameof(Name));
                _main.NotifyAccountChanged();
            }
        }
    }

    public sealed class TokenRow : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly IChain _chain;
        private readonly Asset _asset;

        public TokenRow(MainViewModel main, IChain chain, Asset asset)
        {
            _main = main;
            _chain = chain;
            _asset = asset;
            RemoveCommand = new RelayCommand(() => _main.RemoveCustomToken(_chain, _asset));
        }

        public RelayCommand RemoveCommand { get; }
        public string Symbol => _asset.Symbol;
        public string Network => _chain.Name;
        public string Contract => Formatters.ShortAddress(_asset.Contract);
    }
}
