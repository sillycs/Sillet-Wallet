using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.Core;

namespace Sillet.App.ViewModels
{
    public sealed class ActivityItem
    {
        public ActivityItem(HistoryEntry entry, AssetItem asset, SwapRecord swap = null)
        {
            Entry = entry;
            Asset = asset;
            Swap = swap;
        }

        public SwapRecord Swap { get; }

        public HistoryEntry Entry { get; }
        public AssetItem Asset { get; }
        public string Symbol => Entry.Symbol;
        public string Initial => Lookup.Initial(Entry.Symbol);
        public string IconPath => Asset == null ? null : Asset.IconPath;
        public bool IsIncoming => Entry.Incoming;
        public bool IsOutgoing => !Entry.Incoming;
        public string DirectionGlyph => Entry.Incoming ? "\uE896" : "\uE724";
        public bool HasFee => !Entry.Incoming && Entry.Fee > 0;
        public string Title => Entry.Incoming ? Loc.F("Received {0}", Entry.Symbol)
            : Swap != null ? Loc.F("Swap {0} to {1}", Entry.Symbol, Swap.ToSymbol) : Loc.F("Sent {0}", Entry.Symbol);
        public string Network => Entry.Network;
        public string AmountText => (Entry.Incoming ? "+" : "-") + Formatters.Crypto(Entry.Amount) + " " + Entry.Symbol;
        public string ToText => Entry.Incoming ? Loc.F("From {0}", Formatters.ShortAddress(Entry.From)) : Swap != null ? Loc.F("Via {0}", SwapService.Provider) : Loc.F("To {0}", Formatters.ShortAddress(Entry.To));
        public string FeeText => Entry.Incoming ? "" : Loc.F("Fee {0}", Formatters.Crypto(Entry.Fee) + " " + Entry.FeeSymbol);
        public string DateText => Entry.TimeUtc.ToLocalTime().ToString("MMM d, yyyy  HH:mm", Formatters.Display);
        public string TxText => Formatters.ShortAddress(Entry.TxId ?? "");
        public bool HasExplorer => !string.IsNullOrEmpty(Entry.ExplorerUrl);
    }

    public sealed class LanguageChoice
    {
        public LanguageChoice(string code, string name, bool selected)
        {
            Code = code;
            Name = name;
            IsSelected = selected;
        }

        public string Code { get; }
        public string Name { get; }
        public bool IsSelected { get; }
    }

    public sealed class ActivityTotal
    {
        public ActivityTotal(string symbol, decimal amount, int count, AssetItem asset)
        {
            Symbol = symbol;
            AmountText = Formatters.Crypto(amount) + " " + symbol;
            CountText = count == 1 ? Loc.T("1 transfer") : Loc.F("{0} transfers", count);
            Asset = asset;
        }

        public string Symbol { get; }
        public string AmountText { get; }
        public string CountText { get; }
        public AssetItem Asset { get; }
        public string IconPath => Asset == null ? null : Asset.IconPath;
        public string Initial => Lookup.Initial(Symbol);
    }

    public sealed class ActivityViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private IChain _selectedChain;

        public ActivityViewModel(MainViewModel main)
        {
            _main = main;
            Items = new ObservableCollection<ActivityItem>();
            Chains = main.Wallet.Chains.ToList();
            _selectedChain = Chains.FirstOrDefault();
            OpenTxCommand = new RelayCommand(p =>
            {
                var item = p as ActivityItem;
                if (item != null && item.HasExplorer) _main.Ui.OpenUrl(item.Entry.ExplorerUrl);
            });
            CopyTxCommand = new RelayCommand(p =>
            {
                var item = p as ActivityItem;
                if (item == null || string.IsNullOrEmpty(item.Entry.TxId)) return;
                _main.CopyText(item.Entry.TxId);
                _main.ShowToast(Loc.T("Transaction id copied"));
            });
            CopyAddressCommand = new RelayCommand(() =>
            {
                if (SelectedChain == null || SelectedChain.Address == null) return;
                _main.CopyText(SelectedChain.Address);
                _main.ShowToast(Loc.T("Address copied"));
            });
            Totals = new ObservableCollection<ActivityTotal>();
            Fees = new ObservableCollection<ActivityTotal>();
            ReceiptRows = new ObservableCollection<ReceiptRow>();
            OpenReceiptCommand = new RelayCommand(p => ShowReceipt(p as ActivityItem));
            TrackPaymentCommand = new RelayCommand(p => TrackPayment());
            SpeedUpCommand = new RelayCommand(p => Replace(false));
            CancelPaymentCommand = new RelayCommand(p => Replace(true));
            RetryCommand = new RelayCommand(p => Retry());
            CloseReceiptCommand = new RelayCommand(() => IsReceiptOpen = false);
            DownloadReceiptCommand = new RelayCommand(DownloadReceipt);
            CopyRowCommand = new RelayCommand(p =>
            {
                var row = p as ReceiptRow;
                if (row == null || string.IsNullOrEmpty(row.Value)) return;
                _main.CopyText(row.Value);
                _main.ShowToast(Loc.F("{0} copied", row.Label));
            });
            OpenExplorerCommand = new RelayCommand(() =>
            {
                if (SelectedChain != null) _main.Ui.OpenUrl(SelectedChain.ExplorerAddressUrl(SelectedChain.Address));
            });
            Reload();
        }

        public ObservableCollection<ActivityItem> Items { get; }
        public IReadOnlyList<IChain> Chains { get; }
        private readonly List<ActivityItem> _all = new List<ActivityItem>();
        private int _filter;
        public bool IsEmpty => _all.Count == 0;
        public bool HasItems => _all.Count > 0;

        public bool FilterAll
        {
            get { return _filter == 0; }
            set { if (value) SetFilter(0); }
        }

        public bool FilterSent
        {
            get { return _filter == 1; }
            set { if (value) SetFilter(1); }
        }

        public bool FilterReceived
        {
            get { return _filter == 2; }
            set { if (value) SetFilter(2); }
        }

        private void SetFilter(int filter)
        {
            if (_filter == filter) return;
            _filter = filter;
            ApplyFilter();
            Raise(nameof(FilterAll), nameof(FilterSent), nameof(FilterReceived));
        }

        private void ApplyFilter()
        {
            Items.Clear();
            foreach (var item in _all)
            {
                if (_assetSymbol != null && (!string.Equals(item.Symbol, _assetSymbol, StringComparison.OrdinalIgnoreCase) || (_assetChain != null && item.Entry.ChainId != _assetChain))) continue;
                if (_filter == 0 || (_filter == 1 && !item.IsIncoming) || (_filter == 2 && item.IsIncoming)) Items.Add(item);
            }
            Raise(nameof(CountText), nameof(ShowNoMatches));
        }

        private string _assetSymbol;
        private string _assetChain;

        public bool HasAssetFilter => _assetSymbol != null;
        public string AssetFilterText => _assetSymbol == null ? "" : Loc.F("Showing only {0}", _assetSymbol);

        public void ShowAsset(string symbol, string chainId)
        {
            _assetSymbol = string.IsNullOrEmpty(symbol) ? null : symbol;
            _assetChain = string.IsNullOrEmpty(chainId) ? null : chainId;
            _filter = 0;
            ApplyFilter();
            Raise(nameof(HasAssetFilter), nameof(AssetFilterText), nameof(FilterAll), nameof(FilterSent), nameof(FilterReceived));
        }

        public void ClearAssetFilter()
        {
            if (_assetSymbol == null) return;
            ShowAsset(null, null);
        }

        private RelayCommand _clearAssetFilterCommand;
        public RelayCommand ClearAssetFilterCommand => _clearAssetFilterCommand ?? (_clearAssetFilterCommand = new RelayCommand(ClearAssetFilter));

        public bool ShowNoMatches => (_all.Count > 0 || _assetSymbol != null) && Items.Count == 0;
        public string CountText => Items.Count == 1 ? Loc.T("1 transfer") : Loc.F("{0} transfers", Items.Count);

        public IChain SelectedChain
        {
            get { return _selectedChain; }
            set
            {
                if (value == null) return;
                if (Set(ref _selectedChain, value)) Raise(nameof(SelectedAddress));
            }
        }

        private ActivityItem _receipt;
        private bool _isReceiptOpen;

        public ObservableCollection<ReceiptRow> ReceiptRows { get; }
        public RelayCommand OpenReceiptCommand { get; }
        public RelayCommand TrackPaymentCommand { get; }

        private async void TrackPayment()
        {
            var item = _receipt;
            if (item == null || item.IsIncoming || _main.Send == null) return;
            IsReceiptOpen = false;
            _main.Navigate("send");
            await Task.Delay(450);
            _main.Send.ShowTracked(item.Entry);
        }

        public RelayCommand SpeedUpCommand { get; }
        public RelayCommand CancelPaymentCommand { get; }
        public RelayCommand RetryCommand { get; }
        private bool _receiptPending;
        private bool _receiptRetry;
        public bool ReceiptCanSpeedUp => _receiptPending;
        public bool ReceiptCanRetry => _receiptRetry;

        private async void CheckReplaceable(ActivityItem item)
        {
            _receiptPending = false;
            _receiptRetry = false;
            Raise(nameof(ReceiptCanSpeedUp), nameof(ReceiptCanRetry));
            if (item == null || item.IsIncoming || item.Swap != null || string.IsNullOrEmpty(item.Entry.TxId)) return;
            var chain = _main.Wallet.Chains.FirstOrDefault(c => c.Id == item.Entry.ChainId);
            if (chain == null) return;
            var stage = await TxStatus.StageOfAsync(chain, item.Entry.TxId);
            if (_receipt != item) return;
            _receiptPending = stage == TxStage.Pending && chain.SupportsReplacement;
            _receiptRetry = (stage == TxStage.Failed || stage == TxStage.Unknown) && DateTime.UtcNow - item.Entry.TimeUtc > TimeSpan.FromMinutes(10);
            Raise(nameof(ReceiptCanSpeedUp), nameof(ReceiptCanRetry));
        }

        private async void Replace(bool cancel)
        {
            var item = _receipt;
            if (item == null || _main.Send == null) return;
            IsReceiptOpen = false;
            _main.Navigate("send");
            await Task.Delay(450);
            _main.Send.ShowReplacement(item.Entry, cancel);
        }

        private async void Retry()
        {
            var item = _receipt;
            if (item == null || _main.Send == null) return;
            IsReceiptOpen = false;
            _main.Navigate("send");
            await Task.Delay(450);
            _main.Send.PrefillRetry(item.Entry);
        }

        public RelayCommand CloseReceiptCommand { get; }
        public RelayCommand DownloadReceiptCommand { get; }
        public RelayCommand CopyRowCommand { get; }
        public ActivityItem ReceiptItem => _receipt;
        public bool IsReceiptOpen { get { return _isReceiptOpen; } set { Set(ref _isReceiptOpen, value); } }
        public string ReceiptGlyph => ReceiptIncoming ? "\uE896" : "\uE724";
        public bool ReceiptCanTrack => _receipt != null && !_receipt.IsIncoming && !string.IsNullOrEmpty(_receipt.Entry.TxId);
        public bool ReceiptIncoming => _receipt != null && _receipt.IsIncoming;
        public string ReceiptKindText => _receipt == null ? "" : _receipt.IsIncoming ? Loc.T("Received") : _receipt.Swap != null ? Loc.T("Swap") : Loc.T("Sent");
        public string ReceiptAmountText => _receipt == null ? "" : _receipt.AmountText;
        public string ReceiptStatusText => _receipt == null ? "" : _receipt.IsIncoming ? Loc.T("Payment received") : Loc.T("Payment sent");
        public string ReceiptNetworkText => _receipt == null ? "" : _receipt.Network;
        public string ReceiptDateText => _receipt == null ? "" : _receipt.DateText;
        public bool ReceiptHasExplorer => _receipt != null && _receipt.HasExplorer;

        private static string FullDate(HistoryEntry e)
        {
            return e.TimeUtc.ToLocalTime().ToString("MMM d, yyyy  HH:mm:ss", Loc.Instance.Culture);
        }

        private List<ReceiptRow> BuildRows(ActivityItem item)
        {
            var rows = new List<ReceiptRow>();
            var e = item.Entry;
            var amount = Formatters.Crypto(e.Amount) + " " + e.Symbol;
            if (e.Incoming)
            {
                rows.Add(new ReceiptRow(Loc.T("Status"), Loc.T("Credited to your wallet"), false, false, true));
                rows.Add(new ReceiptRow(Loc.T("You received"), amount, false, false, true));
                rows.Add(new ReceiptRow(Loc.T("Network"), e.Network));
                if (!string.IsNullOrEmpty(e.From)) rows.Add(new ReceiptRow(Loc.T("From"), e.From, true, true));
                rows.Add(new ReceiptRow(Loc.T("To"), e.OwnAddress ?? e.To, true, true));
            }
            else
            {
                var contact = _main.Contacts.Items.FirstOrDefault(c => string.Equals(c.Address, e.To, StringComparison.Ordinal));
                rows.Add(new ReceiptRow(Loc.T("Status"), Loc.T("Sent to the network"), false, false, true) { Trackable = true });
                rows.Add(new ReceiptRow(Loc.T("They receive"), amount, false, false, true));
                rows.Add(new ReceiptRow(Loc.T("Network"), e.Network));
                rows.Add(new ReceiptRow(Loc.T("From"), e.OwnAddress ?? e.From, true, true));
                if (contact != null) rows.Add(new ReceiptRow(Loc.T("Contact"), contact.Name) { Trackable = true });
                rows.Add(new ReceiptRow(Loc.T("To"), e.To, true, true));
                if (item.Swap != null) rows.Add(new ReceiptRow(Loc.T("Swap to"), item.Swap.ToSymbol));
                if (e.Fee > 0)
                {
                    var fee = Formatters.Crypto(e.Fee) + " " + e.FeeSymbol;
                    rows.Add(new ReceiptRow(Loc.T("Network fee"), fee));
                    rows.Add(new ReceiptRow(Loc.T("You paid"), e.FeeSymbol == e.Symbol ? Formatters.Crypto(e.Amount + e.Fee) + " " + e.Symbol : amount + "  +  " + fee, false, false, true));
                }
            }
            rows.Add(new ReceiptRow(Loc.T("Date"), FullDate(e)));
            if (!string.IsNullOrEmpty(e.TxId)) rows.Add(new ReceiptRow(Loc.T("Transaction id"), e.TxId, true, true));
            return rows;
        }

        private void ShowReceipt(ActivityItem item)
        {
            if (item == null) return;
            _receipt = item;
            ReceiptRows.Clear();
            foreach (var row in BuildRows(item)) ReceiptRows.Add(row);
            _receiptLanguage = Loc.Instance.Current;
            RebuildLanguageChoices();
            Raise(nameof(ReceiptItem), nameof(ReceiptIncoming), nameof(ReceiptGlyph), nameof(ReceiptKindText), nameof(ReceiptAmountText), nameof(ReceiptStatusText),
                  nameof(ReceiptNetworkText), nameof(ReceiptDateText), nameof(ReceiptHasExplorer), nameof(ReceiptCanTrack), nameof(SelectedReceiptLanguage));
            IsReceiptOpen = true;
            CheckReplaceable(item);
        }

        private string _receiptLanguage = "en";
        public ObservableCollection<LanguageChoice> ReceiptLanguages { get; } = new ObservableCollection<LanguageChoice>();
        public RelayCommand SetReceiptLanguageCommand => new RelayCommand(p => { var code = p as string; if (code != null) ReceiptLanguage = code; });
        public string ReceiptLanguage
        {
            get { return _receiptLanguage; }
            set
            {
                var code = Loc.Normalize(value);
                if (code == _receiptLanguage) return;
                _receiptLanguage = code;
                RebuildLanguageChoices();
                Raise(nameof(ReceiptLanguage), nameof(SelectedReceiptLanguage));
            }
        }

        public LanguageOption SelectedReceiptLanguage
        {
            get { return Loc.Languages.FirstOrDefault(l => l.Code == _receiptLanguage); }
            set { if (value != null) ReceiptLanguage = value.Code; }
        }

        public IReadOnlyList<LanguageOption> AllLanguages => Loc.Languages;

        private void RebuildLanguageChoices()
        {
            ReceiptLanguages.Clear();
            foreach (var l in Loc.Languages) ReceiptLanguages.Add(new LanguageChoice(l.Code, l.Name, l.Code == _receiptLanguage));
        }

        public byte[] BuildReceiptPdf()
        {
            if (_receipt == null) return null;
            var item = _receipt;
            return Loc.With(_receiptLanguage, () =>
            {
                var status = item.IsIncoming ? Loc.T("Payment received") : Loc.T("Payment sent");
                var kind = item.IsIncoming ? Loc.T("Received") : item.Swap != null ? Loc.T("Swap") : Loc.T("Sent");
                var doc = new ReceiptDocument
                {
                    Heading = Loc.T("Transaction receipt"),
                    Status = status,
                    Kind = kind,
                    Amount = item.AmountText,
                    Incoming = item.IsIncoming,
                    Footer = Loc.T("This receipt was generated by Sillet from the data of your wallet. The blockchain is the final record of the transaction."),
                    Generated = Loc.F("Generated {0}", DateTime.Now.ToString("MMM d, yyyy  HH:mm", Loc.Instance.Culture))
                };
                var statusLabel = Loc.T("Status");
                foreach (var row in BuildRows(item))
                {
                    if (row.Label == statusLabel) continue;
                    doc.Rows.Add(new KeyValuePair<string, string>(row.Label, row.Value));
                    if (row.Mono) doc.Mono.Add(row.Label);
                }
                return ReceiptPdf.Build(doc);
            });
        }

        private void DownloadReceipt()
        {
            if (_receipt == null) return;
            var data = BuildReceiptPdf();
            var tx = _receipt.Entry.TxId;
            var name = "sillet-receipt-" + _receipt.Entry.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd-HHmm", System.Globalization.CultureInfo.InvariantCulture)
                + (string.IsNullOrEmpty(tx) ? "" : "-" + tx.Substring(0, Math.Min(8, tx.Length)).Replace("/", "")) + ".pdf";
            try
            {
                if (_main.Ui.SaveFile(name, "PDF|*.pdf", data)) _main.ShowToast(Loc.T("Receipt saved"));
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                _main.ShowToast(Loc.T("Could not save the file"), true);
            }
        }

        public RelayCommand OpenTxCommand { get; }
        public RelayCommand CopyTxCommand { get; }
        public RelayCommand OpenExplorerCommand { get; }
        public RelayCommand CopyAddressCommand { get; }
        public ObservableCollection<ActivityTotal> Totals { get; }
        public ObservableCollection<ActivityTotal> Fees { get; }
        public bool HasFees => Fees.Count > 0;
        public string ReceivedCountText => _all.Count(i => i.IsIncoming).ToString();
        public string SwapCountText => _all.Count(i => i.Swap != null).ToString();
        public string SelectedAddress => SelectedChain == null ? "" : SelectedChain.Address ?? "";
        public string LastText => _all.Count == 0 ? "-" : _all[0].DateText;
        public string NetworksUsedText => _all.Select(i => i.Network).Distinct().Count().ToString();
        public string TransfersText => _all.Count.ToString();
        public bool HasTotals => Totals.Count > 0;

        public void Reload()
        {
            var mine = new HashSet<string>(_main.Wallet.Chains.Select(c => c.Address).Where(a => a != null));
            var entries = _main.History.Entries.Where(e => mine.Contains(e.OwnAddress)).ToList();
            if (entries.SequenceEqual(_all.Select(i => i.Entry))) return;
            var assets = new Dictionary<string, AssetItem>();
            foreach (var a in _main.AllAssets)
            {
                var key = a.Chain.Id + "|" + a.Symbol;
                if (!assets.ContainsKey(key)) assets[key] = a;
            }
            var list = entries.Select(e =>
            {
                AssetItem asset;
                assets.TryGetValue(e.ChainId + "|" + e.Symbol, out asset);
                return new ActivityItem(e, asset, e.Incoming ? null : _main.Swaps.ByDeposit(e.To));
            }).ToList();
            _all.Clear();
            _all.AddRange(list);
            ApplyFilter();
            var sent = list.Where(i => !i.IsIncoming).ToList();
            Fees.Clear();
            foreach (var g in sent.GroupBy(i => i.Entry.FeeSymbol).OrderByDescending(g => g.Count()))
                Fees.Add(new ActivityTotal(g.Key, g.Sum(i => i.Entry.Fee), g.Count(), _main.AllAssets.FirstOrDefault(a => a.Symbol == g.Key && a.Asset.IsNative)));
            Totals.Clear();
            foreach (var g in sent.GroupBy(i => i.Symbol).OrderByDescending(g => g.Count()))
                Totals.Add(new ActivityTotal(g.Key, g.Sum(i => i.Entry.Amount), g.Count(), g.First().Asset));
            Raise(nameof(IsEmpty), nameof(HasItems), nameof(CountText), nameof(LastText), nameof(NetworksUsedText), nameof(TransfersText), nameof(HasTotals), nameof(HasFees),
                  nameof(SwapCountText), nameof(ReceivedCountText));
            if (IsReceiptOpen && _receipt != null && !list.Any(i => i.Entry == _receipt.Entry)) IsReceiptOpen = false;
        }
    }
}
