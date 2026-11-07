// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.Core;

namespace Sillet.App.ViewModels
{
    public sealed class ReceiptRow
    {
        public ReceiptRow(string label, string value, bool copyable = false, bool mono = false, bool strong = false, string icon = null)
        {
            Label = label;
            Value = value;
            Copyable = copyable;
            Mono = mono;
            Strong = strong;
            Icon = icon;
        }

        public string Label { get; }
        public string Value { get; }
        public bool Copyable { get; }
        public bool Mono { get; }
        public bool Strong { get; }
        public string Icon { get; }
        public bool Trackable { get; set; }
    }

    public sealed class RecipientItem
    {
        public RecipientItem(string address, DateTime timeUtc, string symbol, decimal amount)
        {
            Address = address;
            ShortAddress = Formatters.ShortAddress(address);
            DetailText = Formatters.Crypto(amount) + " " + symbol + "  ·  " + timeUtc.ToLocalTime().ToString("MMM d", Formatters.Display);
        }

        public string Address { get; }
        public string ShortAddress { get; }
        public string DetailText { get; }
    }

    public sealed class SendViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private AssetItem _selected;
        private CoinGroup _selectedCoin;
        private FeeSpeed _feeSpeed = FeeSpeed.Normal;
        private string _confirmPassword = "";
        private string _addressCheck = "";
        private bool _requiresAddressCheck;
        private bool _showContacts;
        private string _contactName = "";
        private readonly List<CoinGroup> _allCoins;
        private bool _rebuilding;
        private string _to = "";
        private string _amountText = "";
        private bool _useMax;
        private int _step;
        private bool _isBusy;
        private string _error;
        private PreparedTransfer _prepared;
        private TransferResult _result;

        public SendViewModel(MainViewModel main)
        {
            _main = main;
            Assets = new ObservableCollection<AssetItem>(main.AllAssets);
            _allCoins = Lookup.GroupByCoin(main.AllAssets);
            Coins = new ObservableCollection<CoinGroup>();
            Notes = new ObservableCollection<string>();
            main.DataChanged += OnDataChanged;
            ReviewCommand = new AsyncCommand(ReviewAsync, () => CanReview);
            ConfirmCommand = new AsyncCommand(ConfirmAsync);
            ConfirmWithHelloCommand = new AsyncCommand(ConfirmWithHelloAsync);
            MaxCommand = new RelayCommand(() =>
            {
                if (SelectedAsset == null) return;
                _amountText = AmountInFiat ? FiatText(SelectedAsset.Balance * SelectedAsset.Price) : Formatters.Crypto(SelectedAsset.Balance);
                _useMax = true;
                Raise(nameof(AmountText), nameof(UseMax), nameof(FiatEquivalentText));
                ReviewCommand.Refresh();
            });
            ToggleAmountModeCommand = new RelayCommand(ToggleAmountMode);
            PasteCommand = new RelayCommand(() =>
            {
                var text = _main.Ui.ReadClipboard();
                if (text != null) ToAddress = text.Trim();
            });
            BackCommand = new RelayCommand(() =>
            {
                Step = 0;
                _prepared = null;
                Error = null;
            });
            NewCommand = new RelayCommand(Reset);
            _feeSpeed = main.Settings.DefaultFeeSpeed == "fast" ? FeeSpeed.Fast : main.Settings.DefaultFeeSpeed == "slow" ? FeeSpeed.Slow : FeeSpeed.Normal;
            Recipients = new ObservableCollection<RecipientItem>();
            ContactList = new ObservableCollection<Contact>();
            SaveContactCommand = new RelayCommand(SaveContact);
            DeleteContactCommand = new RelayCommand(p =>
            {
                var c = p as Contact;
                if (c == null) return;
                _main.Contacts.Remove(c);
                LoadRecipients();
                _main.ShowToast(Loc.T("Contact removed"));
            });
            UseContactCommand = new RelayCommand(p =>
            {
                var c = p as Contact;
                if (c != null) ToAddress = c.Address;
            });
            ShowRecentCommand = new RelayCommand(() => ShowContacts = false);
            ShowContactsCommand = new RelayCommand(() => ShowContacts = true);
            UseRecipientCommand = new RelayCommand(p =>
            {
                var r = p as RecipientItem;
                if (r != null) ToAddress = r.Address;
            });
            ScanFileCommand = new RelayCommand(() => ApplyScan(_main.Ui.ScanQrFromFile(), false));
            ScanClipboardCommand = new RelayCommand(() => ApplyScan(_main.Ui.ScanQrFromClipboard(), true));
            OpenExplorerCommand = new RelayCommand(() =>
            {
                if (_result != null) _main.Ui.OpenUrl(_result.ExplorerUrl);
                else if (!string.IsNullOrEmpty(_trackedUrl)) _main.Ui.OpenUrl(_trackedUrl);
            });
            CopyTxCommand = new RelayCommand(() =>
            {
                if (_result == null) return;
                _main.CopyText(_result.TxId);
                _main.ShowToast(Loc.T("Id copied"));
            });
            Receipt = new ObservableCollection<ReceiptRow>();
            CopyRowCommand = new RelayCommand(p =>
            {
                var row = p as ReceiptRow;
                if (row == null || string.IsNullOrEmpty(row.Value)) return;
                _main.CopyText(row.Value);
                _main.ShowToast(Loc.F("{0} copied", row.Label));
            });
            OnDataChanged();
        }

        public ObservableCollection<ReceiptRow> Receipt { get; }
        public RelayCommand CopyRowCommand { get; }
        public string ResultAmountText => _fake ? _fakeAmount : ReviewAmountText;
        public string ResultFiatText => _fake ? _fakeFiat : ReviewFiatText;
        public bool HasResultFiat => ResultFiatText.Length > 0;

        private TxStage _stage = TxStage.Pending;
        private CancellationTokenSource _track;
        private bool _fake;
        private string _fakeAmount = "";
        private string _fakeFiat = "";

        public TxStage Stage => _stage;
        public bool IsStageFailed => _stage == TxStage.Failed;
        public bool IsStageFinal => _stage == TxStage.Final;
        public string StageTitle => TxStatus.Sent(_stage);
        public string StageHint => TxStatus.SentHint(_stage);
        public bool S1Active => _stage == TxStage.Pending || _stage == TxStage.Unknown;
        public bool S1Done => _stage == TxStage.Confirmed || _stage == TxStage.Final || _stage == TxStage.Failed;
        public bool S2Active => _stage == TxStage.Confirmed;
        public bool S2Done => _stage == TxStage.Final;
        public bool S3Active => false;
        public bool S3Done => _stage == TxStage.Final;

        private void SetStage(TxStage stage)
        {
            if (_stage == stage) return;
            _stage = stage;
            Raise(nameof(Stage), nameof(IsStageFailed), nameof(IsStageFinal), nameof(StageTitle), nameof(StageHint),
                  nameof(S1Active), nameof(S1Done), nameof(S2Active), nameof(S2Done), nameof(S3Active), nameof(S3Done));
            if (Receipt.Count > 0 && Receipt[0].Label == Loc.T("Status"))
                Receipt[0] = new ReceiptRow(Loc.T("Status"), StageTitle, false, false, true, stage == TxStage.Failed ? "Close" : stage == TxStage.Pending ? "Refresh" : "Check");
        }

        private void StopTracking()
        {
            var cts = _track;
            _track = null;
            if (cts != null) cts.Cancel();
        }

        private async void TrackAsync(IChain chain, string txId, string amountText, string network, bool notify = true)
        {
            StopTracking();
            var cts = _track = new CancellationTokenSource();
            var started = DateTime.UtcNow;
            try
            {
                while (!cts.IsCancellationRequested && DateTime.UtcNow - started < TimeSpan.FromHours(2))
                {
                    await Task.Delay(DateTime.UtcNow - started < TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(15), cts.Token);
                    var stage = await chain.GetTxStageAsync(txId, cts.Token);
                    if (stage == TxStage.Unknown) continue;
                    SetStage(stage);
                    if (stage == TxStage.Final || stage == TxStage.Failed)
                    {
                        if (notify) _main.Notify("payment", stage == TxStage.Final ? Loc.T("Payment sent") : Loc.T("Payment failed"),
                            Loc.F("{0} on {1}", amountText, network) + "\n" + Loc.F("Status: {0}", TxStatus.Sent(stage)), "activity");
                        return;
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException || e is WalletException || e is HttpRequestException)
            {
            }
        }

        private string _trackedUrl;
        private HistoryEntry _replacing;

        private AssetItem AssetFor(HistoryEntry e)
        {
            return _main.AllAssets.FirstOrDefault(a => a.Chain.Id == e.ChainId && string.Equals(a.Asset.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                ?? _main.AllAssets.FirstOrDefault(a => a.Chain.Id == e.ChainId && a.Asset.IsNative);
        }

        public async void ShowReplacement(HistoryEntry e, bool cancel)
        {
            var asset = AssetFor(e);
            if (asset == null || string.IsNullOrEmpty(e.TxId)) return;
            Reset();
            SelectedAsset = Assets.FirstOrDefault(a => a == asset) ?? asset;
            IsBusy = true;
            try
            {
                var prepared = await asset.Chain.PrepareReplacementAsync(e.TxId, cancel, asset.Asset, e.Amount);
                _prepared = prepared;
                _replacing = e;
                Notes.Clear();
                foreach (var n in prepared.Notes) Notes.Add(Loc.T(n));
                AddressCheck = "";
                RequiresAddressCheck = false;
                Raise(nameof(ReviewAmountText), nameof(ReviewFiatText), nameof(ReviewToText), nameof(ReviewHistoryNote), nameof(ReviewIsNewAddress), nameof(ReviewNetworkText),
                      nameof(ReviewFeeText), nameof(ReviewTotalText), nameof(HasReviewTotal), nameof(ReviewYouPayText));
                Step = 1;
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Error = MainViewModel.Friendly(ex);
                _main.ShowToast(Error, true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void PrefillRetry(HistoryEntry e)
        {
            var asset = AssetFor(e);
            if (asset == null) return;
            Reset();
            SelectedAsset = Assets.FirstOrDefault(a => a == asset) ?? asset;
            ToAddress = e.To;
            _amountInFiat = false;
            AmountText = e.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public void ShowTracked(HistoryEntry e)
        {
            Reset();
            _fake = true;
            _trackedUrl = e.ExplorerUrl;
            _fakeAmount = Formatters.Crypto(e.Amount) + " " + e.Symbol;
            _fakeFiat = "";
            _stage = TxStage.Pending;
            var contact = _main.Contacts.Items.FirstOrDefault(c => string.Equals(c.Address, e.To, StringComparison.Ordinal));
            Receipt.Clear();
            Receipt.Add(new ReceiptRow(Loc.T("Status"), TxStatus.Sent(TxStage.Pending), false, false, true, "Refresh"));
            Receipt.Add(new ReceiptRow(Loc.T("They receive"), _fakeAmount, false, false, true, "Receive"));
            Receipt.Add(new ReceiptRow(Loc.T("Network"), e.Network, false, false, false, "Globe"));
            Receipt.Add(new ReceiptRow(Loc.T("From"), e.OwnAddress ?? e.From, true, true, false, "Wallet"));
            if (contact != null) Receipt.Add(new ReceiptRow(Loc.T("Contact"), contact.Name, false, false, false, "User"));
            Receipt.Add(new ReceiptRow(Loc.T("To"), e.To, true, true, false, "Forward"));
            if (e.Fee > 0) Receipt.Add(new ReceiptRow(Loc.T("Network fee"), Formatters.Crypto(e.Fee) + " " + e.FeeSymbol, false, false, false, "Swap"));
            Receipt.Add(new ReceiptRow(Loc.T("Date"), e.TimeUtc.ToLocalTime().ToString("MMM d, yyyy  HH:mm:ss", Formatters.Display), false, false, false, "Activity"));
            if (!string.IsNullOrEmpty(e.TxId)) Receipt.Add(new ReceiptRow(Loc.T("Transaction id"), e.TxId, true, true, false, "Shield"));
            Raise(nameof(ResultAmountText), nameof(ResultFiatText), nameof(HasResultFiat), nameof(Stage), nameof(IsStageFailed), nameof(IsStageFinal), nameof(StageTitle), nameof(StageHint),
                  nameof(S1Active), nameof(S1Done), nameof(S2Active), nameof(S2Done), nameof(S3Active), nameof(S3Done));
            Step = 2;
            if (string.IsNullOrEmpty(e.TxId)) return;
            if (e.TxId.StartsWith("dev-", StringComparison.Ordinal))
            {
                var cts = _track = new CancellationTokenSource();
                var ignored = FakeStagesAsync(cts, false);
                return;
            }
            var chain = _main.Wallet.Chains.FirstOrDefault(c => c.Id == e.ChainId);
            if (chain != null) FollowAsync(chain, e.TxId, _fakeAmount, e.Network);
        }

        private async void FollowAsync(IChain chain, string txId, string amountText, string network)
        {
            var cts = new CancellationTokenSource();
            StopTracking();
            _track = cts;
            try
            {
                var now = await chain.GetTxStageAsync(txId, cts.Token);
                if (cts.IsCancellationRequested) return;
                if (now != TxStage.Unknown) SetStage(now);
                if (now == TxStage.Final || now == TxStage.Failed) return;
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is WalletException || ex is HttpRequestException)
            {
            }
            if (!cts.IsCancellationRequested) TrackAsync(chain, txId, amountText, network, false);
        }

        public void ShowFakeSent(string symbol, decimal amount, string network, string from, string to, string txId, decimal fee)
        {
            Reset();
            _fake = true;
            _fakeAmount = Formatters.Crypto(amount) + " " + symbol;
            _fakeFiat = "";
            _stage = TxStage.Pending;
            Receipt.Clear();
            Receipt.Add(new ReceiptRow(Loc.T("Status"), TxStatus.Sent(TxStage.Pending), false, false, true, "Refresh"));
            Receipt.Add(new ReceiptRow(Loc.T("They receive"), _fakeAmount, false, false, true, "Receive"));
            Receipt.Add(new ReceiptRow(Loc.T("Network"), network, false, false, false, "Globe"));
            Receipt.Add(new ReceiptRow(Loc.T("From"), from, true, true, false, "Wallet"));
            Receipt.Add(new ReceiptRow(Loc.T("To"), to, true, true, false, "Forward"));
            Receipt.Add(new ReceiptRow(Loc.T("Network fee"), Formatters.Crypto(fee) + " " + symbol, false, false, false, "Swap"));
            Receipt.Add(new ReceiptRow(Loc.T("Date"), DateTime.Now.ToString("MMM d, yyyy  HH:mm:ss", Formatters.Display), false, false, false, "Activity"));
            Receipt.Add(new ReceiptRow(Loc.T("Transaction id"), txId, true, true, false, "Shield"));
            Raise(nameof(ResultAmountText), nameof(ResultFiatText), nameof(HasResultFiat), nameof(Stage), nameof(IsStageFailed), nameof(IsStageFinal), nameof(StageTitle), nameof(StageHint),
                  nameof(S1Active), nameof(S1Done), nameof(S2Active), nameof(S2Done), nameof(S3Active), nameof(S3Done));
            Step = 2;
            var cts = _track = new CancellationTokenSource();
            var ignored = FakeStagesAsync(cts);
        }

        private async Task FakeStagesAsync(CancellationTokenSource cts, bool notify = true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                SetStage(TxStage.Confirmed);
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                SetStage(TxStage.Final);
                if (notify) _main.Notify("payment", Loc.T("Payment sent"), Loc.F("{0} on {1}", _fakeAmount, Loc.T("Test network")) + "\n" + Loc.F("Status: {0}", TxStatus.Sent(TxStage.Final)), "activity");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void BuildReceipt(DateTime sentAt)
        {
            Receipt.Clear();
            if (_prepared == null || _result == null) return;
            var contact = _main.Contacts.Items.FirstOrDefault(c => string.Equals(c.Address, _prepared.To, StringComparison.Ordinal));
            Receipt.Add(new ReceiptRow(Loc.T("Status"), TxStatus.Sent(_stage), false, false, true, "Refresh"));
            Receipt.Add(new ReceiptRow(Loc.T("They receive"), ReviewAmountText + (HasResultFiat ? "  (" + ReviewFiatText + ")" : ""), false, false, true, "Receive"));
            Receipt.Add(new ReceiptRow(Loc.T("Network"), _prepared.Chain.Name, false, false, false, "Globe"));
            Receipt.Add(new ReceiptRow(Loc.T("From"), _prepared.Chain.Address, true, true, false, "Wallet"));
            if (contact != null) Receipt.Add(new ReceiptRow(Loc.T("Contact"), contact.Name, false, false, false, "User"));
            Receipt.Add(new ReceiptRow(Loc.T("To"), _prepared.To, true, true, false, "Forward"));
            Receipt.Add(new ReceiptRow(Loc.T("Network fee"), ReviewFeeText, false, false, false, "Swap"));
            Receipt.Add(new ReceiptRow(Loc.T("You paid"), ReviewYouPayText, false, false, true, "Send"));
            Receipt.Add(new ReceiptRow(Loc.T("Date"), sentAt.ToLocalTime().ToString("MMM d, yyyy  HH:mm:ss", Formatters.Display), false, false, false, "Activity"));
            Receipt.Add(new ReceiptRow(Loc.T("Transaction id"), _result.TxId, true, true, false, "Shield"));
        }

        public ObservableCollection<AssetItem> Assets { get; }
        public ObservableCollection<CoinGroup> Coins { get; }

        public CoinGroup SelectedCoin
        {
            get { return _selectedCoin; }
            set
            {
                if (value == null && _rebuilding) return;
                if (!Set(ref _selectedCoin, value)) return;
                Raise(nameof(Networks), nameof(HasManyNetworks));
                if (value != null && (SelectedAsset == null || !value.Assets.Contains(SelectedAsset))) SelectedAsset = value.BestNetwork();
            }
        }

        public IReadOnlyList<AssetItem> Networks => SelectedCoin == null ? new AssetItem[0] : SelectedCoin.Assets;
        public bool HasManyNetworks => SelectedCoin != null && SelectedCoin.NetworkCount > 1;

        public AssetItem SelectedAsset
        {
            get { return _selected; }
            set
            {
                if (value == null && SelectedCoin != null) return;
                if (!Set(ref _selected, value)) return;
                if (value != null && (_selectedCoin == null || _selectedCoin.Symbol != value.Symbol))
                {
                    _selectedCoin = Coins.FirstOrDefault(c => c.Symbol == value.Symbol) ?? _allCoins.FirstOrDefault(c => c.Symbol == value.Symbol);
                    Raise(nameof(SelectedCoin), nameof(Networks), nameof(HasManyNetworks));
                }
                _useMax = false;
                _amountText = "";
                Error = null;
                LoadRecipients();
                Raise(nameof(SupportsFeeSpeed), nameof(CanToggleAmountMode), nameof(AmountInFiat), nameof(AmountUnit), nameof(OtherUnit));
                Raise(nameof(AmountText), nameof(UseMax), nameof(AvailableText), nameof(FiatEquivalentText), nameof(AddressHint),
                      nameof(AddressError), nameof(IsAddressValid), nameof(HasAddressError), nameof(HasSelection), nameof(FeeHint), nameof(HasFeeHint));
                ReviewCommand.Refresh();
            }
        }

        public bool HasSelection => SelectedAsset != null;

        public string AvailableText
        {
            get
            {
                if (SelectedAsset == null) return "";
                if (_main.HideBalances) return Loc.F("Available {0}", Formatters.Hidden);
                return Loc.F("Available {0}", Formatters.Crypto(SelectedAsset.Balance) + " " + SelectedAsset.Symbol);
            }
        }

        public string AddressHint => SelectedAsset == null ? Loc.T("Address") : Loc.F("Address on {0}", SelectedAsset.Network);
        public string FeeHint => SelectedAsset != null && SelectedAsset.IsToken ? Loc.F("Fee in {0}", SelectedAsset.Chain.Native.Symbol) : "";
        public bool HasFeeHint => FeeHint.Length > 0;

        public string ToAddress
        {
            get { return _to; }
            set
            {
                if (!Set(ref _to, value == null ? "" : value.Trim())) return;
                Raise(nameof(IsAddressValid), nameof(AddressError), nameof(HasAddressError));
                Raise(nameof(CanSaveContact), nameof(ToContactName), nameof(HasToContact));
                ReviewCommand.Refresh();
            }
        }

        public bool IsAddressValid => SelectedAsset != null && ToAddress.Length > 0 && SelectedAsset.Chain.IsValidAddress(ToAddress);

        public string AddressError => SelectedAsset == null || ToAddress.Length == 0 || IsAddressValid
            ? null
            : Loc.F("Invalid address for {0}", SelectedAsset.Network);

        public bool HasAddressError => AddressError != null;

        public string AmountText
        {
            get { return _amountText; }
            set
            {
                if (!Set(ref _amountText, value)) return;
                _useMax = false;
                Raise(nameof(UseMax), nameof(FiatEquivalentText));
                ReviewCommand.Refresh();
            }
        }

        public bool UseMax => _useMax;

        private bool _amountInFiat;

        public bool AmountInFiat => _amountInFiat && CanToggleAmountMode;
        public bool CanToggleAmountMode => SelectedAsset != null && SelectedAsset.Price > 0;
        public RelayCommand ToggleAmountModeCommand { get; private set; }

        public string AmountUnit => SelectedAsset == null ? "" : AmountInFiat ? _main.Currency : SelectedAsset.Symbol;

        public string OtherUnit => SelectedAsset == null ? "" : AmountInFiat ? SelectedAsset.Symbol : _main.Currency;

        private static string FiatText(decimal value)
        {
            return Math.Round(value, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void ToggleAmountMode()
        {
            if (!CanToggleAmountMode) return;
            var crypto = CryptoAmount;
            _amountInFiat = !_amountInFiat;
            if (!_useMax && crypto > 0)
                _amountText = _amountInFiat ? FiatText(crypto * SelectedAsset.Price) : Formatters.Crypto(crypto);
            else if (_useMax)
                _amountText = _amountInFiat ? FiatText(SelectedAsset.Balance * SelectedAsset.Price) : Formatters.Crypto(SelectedAsset.Balance);
            Raise(nameof(AmountText), nameof(AmountInFiat), nameof(AmountUnit), nameof(OtherUnit), nameof(FiatEquivalentText));
            ReviewCommand.Refresh();
        }

        private decimal CryptoAmount
        {
            get
            {
                decimal a;
                if (!Formatters.TryParseAmount(AmountText, out a)) return 0;
                if (!AmountInFiat) return a;
                var price = SelectedAsset == null ? 0 : SelectedAsset.Price;
                return price > 0 ? Math.Round(a / price, 8) : 0;
            }
        }

        private decimal Amount
        {
            get { return CryptoAmount; }
        }

        public string FiatEquivalentText
        {
            get
            {
                if (SelectedAsset == null || SelectedAsset.Price == 0) return "";
                var text = AmountInFiat
                    ? "≈ " + Formatters.Crypto(Amount) + " " + SelectedAsset.Symbol
                    : "≈ " + Formatters.Fiat(Amount * SelectedAsset.Price, _main.Currency);
                return UseMax && SelectedAsset.Asset.IsNative ? text + Loc.T(", minus fee") : text;
            }
        }

        public bool CanReview => SelectedAsset != null && IsAddressValid && (UseMax || Amount > 0) && !IsBusy;

        public int Step
        {
            get { return _step; }
            private set
            {
                if (Set(ref _step, value)) Raise(nameof(IsFormStep), nameof(IsReviewStep), nameof(IsDoneStep), nameof(RequiresPassword), nameof(CanUseHello));
            }
        }

        public bool IsFormStep => Step == 0;
        public bool IsReviewStep => Step == 1;
        public bool IsDoneStep => Step == 2;

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (Set(ref _isBusy, value)) ReviewCommand.Refresh();
            }
        }

        public string Error
        {
            get { return _error; }
            private set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(Error);

        public string ReviewAmountText => _prepared == null ? "" : Formatters.Crypto(_prepared.Amount) + " " + _prepared.Asset.Symbol;

        public string ReviewFiatText => _prepared == null || SelectedAsset == null || SelectedAsset.Price == 0
            ? ""
            : "≈ " + Formatters.Fiat(_prepared.Amount * SelectedAsset.Price, _main.Currency);

        public string ReviewHistoryNote
        {
            get
            {
                if (_prepared == null) return "";
                var to = _prepared.To;
                var sent = _main.History.Entries.Where(e => !e.Incoming && string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.TimeUtc).ToList();
                if (sent.Count == 0) return Loc.T("First time sending to this address. Check it carefully");
                return Loc.F("You have sent here {0} times. Last: {1}", sent.Count, sent[0].TimeUtc.ToLocalTime().ToString("MMM d, yyyy", Formatters.Display));
            }
        }
        public bool ReviewIsNewAddress => _prepared != null && !_main.History.Entries.Any(e => !e.Incoming && string.Equals(e.To, _prepared.To, StringComparison.OrdinalIgnoreCase));
        public string ReviewToText => _prepared == null ? "" : _prepared.To;
        public string ReviewNetworkText => _prepared == null ? "" : _prepared.Chain.Name;

        public string ReviewFeeText
        {
            get
            {
                if (_prepared == null) return "";
                var text = Formatters.Crypto(_prepared.Fee) + " " + _prepared.FeeAsset.Symbol;
                var native = _main.AllAssets.FirstOrDefault(a => a.Chain == _prepared.Chain && a.Asset.IsNative);
                var nativePrice = native == null ? 0 : native.Price;
                if (_prepared.Chain.SupportsFeeSpeed && _feeSpeed != FeeSpeed.Normal) text += _feeSpeed == FeeSpeed.Fast ? Loc.T("  · fast") : Loc.T("  · slow");
                return nativePrice > 0 ? text + "  (≈ " + Formatters.Fiat(_prepared.Fee * nativePrice, _main.Currency) + ")" : text;
            }
        }

        public string ReviewTotalText => _prepared != null && _prepared.Asset == _prepared.FeeAsset
            ? Formatters.Crypto(_prepared.Amount + _prepared.Fee) + " " + _prepared.Asset.Symbol
            : "";

        public bool HasReviewTotal => ReviewTotalText.Length > 0;

        public string ReviewYouPayText
        {
            get
            {
                if (_prepared == null) return "";
                if (HasReviewTotal) return ReviewTotalText;
                return ReviewAmountText + "  +  " + Formatters.Crypto(_prepared.Fee) + " " + _prepared.FeeAsset.Symbol;
            }
        }
        public ObservableCollection<string> Notes { get; }

        public string ResultTxText => _result == null ? "" : Formatters.ShortAddress(_result.TxId);
        public string ResultSummary => _prepared == null ? "" : Loc.F("{0} to {1}", ReviewAmountText, Formatters.ShortAddress(_prepared.To));

        public AsyncCommand ReviewCommand { get; }
        public AsyncCommand ConfirmCommand { get; }
        public AsyncCommand ConfirmWithHelloCommand { get; }
        public bool CanUseHello => RequiresPassword && _main.Passkey.IsSet;

        private async Task ConfirmWithHelloAsync()
        {
            if (_prepared == null || IsBusy) return;
            Error = null;
            try
            {
                _confirmPassword = await _main.Passkey.GetPasswordAsync(_main.Ui, Loc.T("Confirm the send in Sillet"));
            }
            catch (PasskeyException e)
            {
                if (!e.Cancelled) Error = Loc.T(e.Message);
                return;
            }
            await ConfirmAsync();
        }
        public RelayCommand MaxCommand { get; }
        public RelayCommand PasteCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand NewCommand { get; }
        public ObservableCollection<RecipientItem> Recipients { get; }
        public ObservableCollection<Contact> ContactList { get; }
        public RelayCommand SaveContactCommand { get; }
        public RelayCommand DeleteContactCommand { get; }
        public RelayCommand UseContactCommand { get; }
        public RelayCommand ShowRecentCommand { get; }
        public RelayCommand ShowContactsCommand { get; }
        public bool HasContacts => ContactList.Count > 0;
        public bool ShowRecentEmpty => !ShowContacts && !HasRecipients;
        public bool ShowRecentList => !ShowContacts && HasRecipients;
        public bool ShowContactsEmpty => ShowContacts && !HasContacts;
        public bool ShowContactsList => ShowContacts && HasContacts;
        public bool CanSaveContact => IsAddressValid && _main.Contacts.Find(SelectedAsset.Chain.Id, ToAddress) == null;

        public bool ShowContacts
        {
            get { return _showContacts; }
            set
            {
                if (Set(ref _showContacts, value))
                {
                    Raise(nameof(ShowRecent));
                    RaiseLists();
                }
            }
        }

        public bool ShowRecent
        {
            get { return !_showContacts; }
            set { if (value) ShowContacts = false; }
        }

        public string ContactName
        {
            get { return _contactName; }
            set { Set(ref _contactName, value ?? ""); }
        }

        public string ToContactName
        {
            get
            {
                if (SelectedAsset == null || ToAddress.Length == 0) return "";
                var c = _main.Contacts.Find(SelectedAsset.Chain.Id, ToAddress);
                return c == null ? "" : c.Name;
            }
        }

        public bool HasToContact => ToContactName.Length > 0;

        private void RaiseLists()
        {
            Raise(nameof(HasContacts), nameof(ShowRecentEmpty), nameof(ShowRecentList), nameof(ShowContactsEmpty), nameof(ShowContactsList),
                  nameof(CanSaveContact), nameof(ToContactName), nameof(HasToContact));
        }

        private void SaveContact()
        {
            if (!IsAddressValid) return;
            var name = (ContactName ?? "").Trim();
            if (name.Length == 0)
            {
                _main.ShowToast(Loc.T("Type a name for the contact"), true);
                return;
            }
            _main.Contacts.Add(name, SelectedAsset.Chain.Id, SelectedAsset.Network, ToAddress);
            ContactName = "";
            ShowContacts = true;
            LoadRecipients();
            _main.ShowToast(Loc.T("Contact saved"));
        }

        public bool RequiresPassword => _main.Settings.RequirePasswordToSend;

        public string ConfirmPassword
        {
            get { return _confirmPassword; }
            set { _confirmPassword = value ?? ""; }
        }

        public int ConfirmPasswordVersion { get; private set; }

        public bool RequiresAddressCheck
        {
            get { return _requiresAddressCheck; }
            private set { Set(ref _requiresAddressCheck, value); }
        }

        public string AddressCheck
        {
            get { return _addressCheck; }
            set { Set(ref _addressCheck, (value ?? "").Trim()); }
        }

        public string AddressCheckHint => Loc.T("This is a large send. Type the last 4 characters of the address to confirm");

        public bool IsLargeSend(decimal fiatValue)
        {
            var limit = AppSettings.LargeSendLimit(_main.Settings.LargeSendLevel, _main.PerUsd);
            return limit > 0 && fiatValue >= limit;
        }

        public bool SupportsFeeSpeed => SelectedAsset != null && SelectedAsset.Chain.SupportsFeeSpeed;

        public bool FeeSlow
        {
            get { return _feeSpeed == FeeSpeed.Slow; }
            set { if (value) SetFeeSpeed(FeeSpeed.Slow); }
        }

        public bool FeeNormal
        {
            get { return _feeSpeed == FeeSpeed.Normal; }
            set { if (value) SetFeeSpeed(FeeSpeed.Normal); }
        }

        public bool FeeFast
        {
            get { return _feeSpeed == FeeSpeed.Fast; }
            set { if (value) SetFeeSpeed(FeeSpeed.Fast); }
        }

        public FeeSpeed Speed => _feeSpeed;

        private void SetFeeSpeed(FeeSpeed speed)
        {
            _feeSpeed = speed;
            Raise(nameof(FeeSlow), nameof(FeeNormal), nameof(FeeFast), nameof(Speed));
        }
        public RelayCommand UseRecipientCommand { get; }
        public bool HasRecipients => Recipients.Count > 0;

        public void LoadRecipients()
        {
            Recipients.Clear();
            if (SelectedAsset != null)
            {
                var chain = SelectedAsset.Chain;
                foreach (var e in _main.History.Entries
                    .Where(e => e.ChainId == chain.Id && e.From == chain.Address)
                    .GroupBy(e => e.To)
                    .Select(g => g.First())
                    .Take(6))
                    Recipients.Add(new RecipientItem(e.To, e.TimeUtc, e.Symbol, e.Amount));
            }
            ContactList.Clear();
            if (SelectedAsset != null)
                foreach (var c in _main.Contacts.Items.Where(c => c.ChainId == SelectedAsset.Chain.Id).OrderBy(c => c.Name)) ContactList.Add(c);
            Raise(nameof(HasRecipients));
            RaiseLists();
        }

        public RelayCommand ScanFileCommand { get; }
        public RelayCommand ScanClipboardCommand { get; }

        public void ApplyScan(string text, bool fromClipboard)
        {
            if (text == null)
            {
                if (fromClipboard) _main.ShowToast(Loc.T("Copy a QR image or screenshot first"), true);
                return;
            }
            var request = PaymentUri.Parse(text);
            if (request == null)
            {
                _main.ShowToast(Loc.T("No QR code found in that image"), true);
                return;
            }
            ToAddress = request.Address;
            if (request.Amount.HasValue) _amountInFiat = false;
            if (request.Amount.HasValue) AmountText = request.Amount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _main.ShowToast(IsAddressValid ? Loc.T("Address scanned") : (SelectedAsset == null ? Loc.T("Scanned, but it is not a valid address") : Loc.F("Scanned, but it is not a {0} address", SelectedAsset.Network)), !IsAddressValid);
        }
        public RelayCommand OpenExplorerCommand { get; }
        public RelayCommand CopyTxCommand { get; }

        public void Preselect(AssetItem asset)
        {
            Reset();
            SelectedAsset = Assets.FirstOrDefault(a => a == asset) ?? SelectedAsset;
        }

        private void OnDataChanged()
        {
            if (Step != 0) return;
            foreach (var c in _allCoins) c.UpdateTexts(_main.Currency, _main.HideBalances);
            var ordered = _allCoins
                .OrderByDescending(c => c.HasBalance)
                .ThenByDescending(c => c.TotalFiat)
                .ThenBy(c => Lookup.CoinRank(c.Symbol))
                .ToList();
            if (!ordered.SequenceEqual(Coins))
            {
                var keep = _selectedCoin;
                _rebuilding = true;
                Coins.Clear();
                foreach (var c in ordered) Coins.Add(c);
                _rebuilding = false;
                _selectedCoin = null;
                SelectedCoin = keep ?? Coins.FirstOrDefault(c => c.HasBalance) ?? Coins.FirstOrDefault();
                Raise(nameof(SelectedCoin));
            }
            Raise(nameof(AvailableText), nameof(FiatEquivalentText));
        }

        private static bool IsNetworkError(Exception e)
        {
            return e is WalletException || e is HttpRequestException || e is TaskCanceledException;
        }

        private async Task ReviewAsync()
        {
            if (SelectedAsset == null) return;
            Error = null;
            if (_main.Wallet.IsWatchOnly && !_main.Wallet.CanSignOnDevice(SelectedAsset.Chain))
            {
                Error = Loc.T("This network can't be signed with your hardware wallet here yet. Send from your hardware wallet app");
                return;
            }
            if (_main.Settings.ContactsOnly && _main.Contacts.Find(SelectedAsset.Chain.Id, ToAddress) == null)
            {
                Error = Loc.T("Only saved contacts are allowed. Save this address as a contact first, or turn this off in Settings");
                return;
            }
            IsBusy = true;
            try
            {
                SelectedAsset.Chain.FeeSpeed = _feeSpeed;
                _prepared = await SelectedAsset.Chain.PrepareTransferAsync(SelectedAsset.Asset, ToAddress, Amount, UseMax);
                Notes.Clear();
                if (_main.Settings.WarnLookalike)
                {
                    var imitated = AddressGuard.FindLookalike(_prepared.To, KnownRecipients(SelectedAsset.Chain));
                    if (imitated != null)
                        Notes.Add(Loc.F("Warning: this address looks like {0}, which you used before, but it is a different address. Scammers send tiny payments from lookalikes so you copy them from your history. Compare every character", AddressGuard.Short(imitated)));
                }
                if (_main.Settings.WarnNewAddress && IsNewAddress(_prepared.To))
                    Notes.Add(Loc.T("First time sending to this address. Check every character before you confirm"));
                foreach (var n in _prepared.Notes) Notes.Add(n);
                var feeNative = _main.AllAssets.FirstOrDefault(a => a.Chain == _prepared.Chain && a.Asset.IsNative);
                if (feeNative != null && feeNative.Price > 0 && SelectedAsset.Price > 0 && _prepared.Amount > 0)
                {
                    var feeFiat = _prepared.Fee * feeNative.Price;
                    var amountFiat = _prepared.Amount * SelectedAsset.Price;
                    if (feeFiat > amountFiat * 0.05m && feeFiat >= 0.5m && !Notes.Contains(Loc.T("The fee is more than 10% of the amount")))
                        Notes.Add(Loc.F("The network fee is high: about {0}, which is {1}% of what you send", Formatters.Fiat(feeFiat, _main.Currency), Math.Min(999, (int)Math.Round(feeFiat / amountFiat * 100))));
                }
                AddressCheck = "";
                RequiresAddressCheck = SelectedAsset.Price > 0 && IsLargeSend(_prepared.Amount * SelectedAsset.Price);
                Raise(nameof(ReviewAmountText), nameof(ReviewFiatText), nameof(ReviewToText), nameof(ReviewHistoryNote), nameof(ReviewIsNewAddress), nameof(ReviewNetworkText),
                      nameof(ReviewFeeText), nameof(ReviewTotalText), nameof(HasReviewTotal), nameof(ReviewYouPayText));
                Step = 1;
            }
            catch (Exception e) when (IsNetworkError(e))
            {
                Error = MainViewModel.Friendly(e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ConfirmAsync()
        {
            if (_prepared == null || SelectedAsset == null) return;
            Error = null;
            if (RequiresAddressCheck)
            {
                var to = _prepared.To ?? "";
                var tail = to.Length >= 4 ? to.Substring(to.Length - 4) : to;
                if (!string.Equals(_addressCheck, tail, StringComparison.OrdinalIgnoreCase))
                {
                    Error = Loc.T("The last 4 characters do not match the address");
                    return;
                }
            }
            IsBusy = true;
            if (RequiresPassword)
            {
                var pw = _confirmPassword;
                _confirmPassword = "";
                ConfirmPasswordVersion++;
                Raise(nameof(ConfirmPasswordVersion));
                bool ok;
                try
                {
                    await Task.Run(() => _main.Wallet.RevealMnemonic(pw));
                    ok = true;
                }
                catch (WalletException)
                {
                    ok = false;
                }
                if (!ok)
                {
                    Error = Loc.T("Wrong password");
                    IsBusy = false;
                    return;
                }
            }
            if (_main.Wallet.IsWatchOnly) _main.ShowToast(Loc.T("Confirm the payment on your Ledger"));
            try
            {
                _result = await SelectedAsset.Chain.BroadcastAsync(_prepared);
                _main.RecordSent(_prepared, _result);
                if (_replacing != null)
                {
                    var old = _replacing.TxId;
                    _replacing = null;
                    _main.History.RemoveWhere(x => string.Equals(x.TxId, old, StringComparison.OrdinalIgnoreCase) && !x.Incoming);
                    if (_main.Activity != null) _main.Activity.Reload();
                }
                _stage = TxStage.Pending;
                BuildReceipt(DateTime.UtcNow);
                Raise(nameof(Stage), nameof(IsStageFailed), nameof(IsStageFinal), nameof(StageTitle), nameof(StageHint),
                      nameof(S1Active), nameof(S1Done), nameof(S2Active), nameof(S2Done), nameof(S3Active), nameof(S3Done));
                TrackAsync(SelectedAsset.Chain, _result.TxId, ReviewAmountText, SelectedAsset.Chain.Name);
                Raise(nameof(ResultTxText), nameof(ResultSummary), nameof(ResultAmountText), nameof(ResultFiatText), nameof(HasResultFiat));
                Step = 2;
                _main.ShowToast(Loc.T("Transaction sent"));
                var ignored = RefreshLaterAsync();
            }
            catch (Exception e) when (IsNetworkError(e))
            {
                Error = MainViewModel.Friendly(e) + Loc.T(". Go back and prepare it again");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshLaterAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (_main.IsUnlocked) await _main.RefreshBalancesAsync();
        }

        private IEnumerable<string> KnownRecipients(IChain chain)
        {
            foreach (var c in _main.Contacts.Items)
                if (c.ChainId == chain.Id) yield return c.Address;
            foreach (var e in _main.History.Entries)
                if (!e.Incoming && e.ChainId == chain.Id) yield return e.To;
            foreach (var a in chain.KnownAddresses) yield return a;
        }

        private bool IsNewAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            if (_main.Contacts.Items.Any(c => string.Equals(c.Address, address, StringComparison.Ordinal))) return false;
            return !_main.History.Entries.Any(e => string.Equals(e.To, address, StringComparison.Ordinal));
        }

        private void Reset()
        {
            StopTracking();
            _fake = false;
            _replacing = null;
            _stage = TxStage.Pending;
            Raise(nameof(ResultAmountText), nameof(ResultFiatText), nameof(HasResultFiat), nameof(Stage), nameof(IsStageFailed), nameof(IsStageFinal), nameof(StageTitle), nameof(StageHint),
                  nameof(S1Active), nameof(S1Done), nameof(S2Active), nameof(S2Done), nameof(S3Active), nameof(S3Done));
            _prepared = null;
            _result = null;
            _to = "";
            _amountText = "";
            _useMax = false;
            Error = null;
            Notes.Clear();
            Receipt.Clear();
            Step = 0;
            Raise(nameof(ToAddress), nameof(AmountText), nameof(UseMax), nameof(IsAddressValid), nameof(AddressError),
                  nameof(HasAddressError), nameof(FiatEquivalentText));
            ReviewCommand.Refresh();
        }
    }
}
