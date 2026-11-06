using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.Core;

namespace Sillet.App.ViewModels
{
    public sealed class SwapItem : ObservableObject
    {
        private string _status;

        public SwapItem(SwapRecord record, AssetItem from, AssetItem to)
        {
            Record = record;
            From = from;
            To = to;
            _status = record.Status;
        }

        public SwapRecord Record { get; }
        public AssetItem From { get; }
        public AssetItem To { get; }
        public string FromIcon => From == null ? null : From.IconPath;
        public string ToIcon => To == null ? null : To.IconPath;
        public string FromInitial => Lookup.Initial(Record.FromSymbol);
        public string ToInitial => Lookup.Initial(Record.ToSymbol);
        public string Title => Loc.F("{0} to {1}", Record.FromSymbol, Record.ToSymbol);
        public string AmountText => Formatters.Crypto(Record.Amount) + " " + Record.FromSymbol + "  →  ~" + Formatters.Crypto(Record.AmountTo) + " " + Record.ToSymbol;
        public string DateText => Record.TimeUtc.ToLocalTime().ToString("MMM d, HH:mm", Formatters.Display);

        public string Status
        {
            get { return _status; }
            set
            {
                if (Set(ref _status, value)) Raise(nameof(StatusText), nameof(IsDone), nameof(IsFailed), nameof(IsPending));
            }
        }

        public string StatusText => SwapService.StatusText(Status);
        public bool IsDone => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
        public bool IsFailed => SwapService.IsFinal(Status) && !IsDone;
        public bool IsPending => !SwapService.IsFinal(Status);
    }

    public sealed class RampProvider : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly string _description;

        public RampProvider(string name, string description, string buyUrl, string sellUrl, string initial)
        {
            Name = name;
            _description = description;
            Loc.Changed += () =>
            {
                var handler = PropertyChanged;
                if (handler != null) handler(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Description)));
            };
            BuyUrl = buyUrl;
            SellUrl = sellUrl;
            Initial = initial;
        }

        public string Name { get; }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Description => Loc.T(_description);
        public string BuyUrl { get; }
        public string SellUrl { get; }
        public string Initial { get; }
    }

    public sealed class TradeViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private int _tab;
        private AssetItem _from;
        private AssetItem _to;
        private string _amountText = "";
        private SwapQuote _quote;
        private string _quoteError;
        private bool _isQuoting;
        private bool _isSwapping;
        private int _quoteVersion;
        private CoinGroup _rampCoin;
        private AssetItem _rampNetwork;

        public TradeViewModel(MainViewModel main)
        {
            _main = main;
            Assets = main.AllAssets.OrderBy(a => Lookup.CoinRank(a.Symbol)).ToList();
            Swaps = new ObservableCollection<SwapItem>();
            Coins = new ObservableCollection<CoinGroup>(Lookup.GroupByCoin(main.AllAssets));
            Providers = new List<RampProvider>
            {
                new RampProvider("MoonPay", "Cards, Apple Pay and bank transfer", "https://www.moonpay.com/buy", "https://www.moonpay.com/sell", "M"),
                new RampProvider("Ramp", "Cards and bank transfer in many countries", "https://ramp.network/buy", "https://ramp.network/sell", "R"),
                new RampProvider("Transak", "Cards and local payment methods", "https://transak.com/buy", "https://transak.com/sell", "T"),
                new RampProvider("Buda", "Chilean exchange, pay with CLP", "https://www.buda.com", "https://www.buda.com", "B"),
                new RampProvider("Orionx", "Chilean exchange, pay with CLP", "https://www.orionx.com", "https://www.orionx.com", "O"),
            };
            _from = Assets.FirstOrDefault(a => a.HasBalance) ?? Assets.FirstOrDefault(a => a.Symbol == "BTC");
            _to = Assets.FirstOrDefault(a => a.Symbol == "USDT" && a.Chain.Id == "tron") ?? Assets.FirstOrDefault(a => a != _from);
            RampCoin = Coins.FirstOrDefault();
            SwitchCommand = new RelayCommand(() =>
            {
                var f = _from;
                _from = _to;
                _to = f;
                _amountText = "";
                OnPairChanged();
            });
            MaxCommand = new RelayCommand(() =>
            {
                if (_from == null) return;
                AmountText = Formatters.Crypto(_from.Balance);
                _useMax = true;
            });
            SwapCommand = new AsyncCommand(SwapAsync, () => CanSwap);
            RefreshSwapsCommand = new AsyncCommand(RefreshSwapsAsync);
            OpenProviderCommand = new RelayCommand(OpenProvider);
            CopyRampAddressCommand = new RelayCommand(() =>
            {
                if (RampAddress.Length == 0) return;
                _main.CopyText(RampAddress);
                _main.ShowToast(Loc.T("Address copied"));
            });
            OpenTermsCommand = new RelayCommand(() => _main.Ui.OpenUrl(SwapService.TermsUrl));
            main.DataChanged += () => Raise(nameof(FromBalanceText), nameof(ToBalanceText), nameof(FromFiatText));
            LoadSwaps();
        }

        public IReadOnlyList<AssetItem> Assets { get; }
        public ObservableCollection<SwapItem> Swaps { get; }
        public ObservableCollection<CoinGroup> Coins { get; }
        public IReadOnlyList<RampProvider> Providers { get; }
        public RelayCommand SwitchCommand { get; }
        public RelayCommand MaxCommand { get; }
        public AsyncCommand SwapCommand { get; }
        public AsyncCommand RefreshSwapsCommand { get; }
        public RelayCommand OpenProviderCommand { get; }
        public RelayCommand CopyRampAddressCommand { get; }
        public RelayCommand OpenTermsCommand { get; }
        public string ProviderName => SwapService.Provider;

        public bool IsSwapTab
        {
            get { return _tab == 0; }
            set { if (value) SetTab(0); }
        }

        public bool IsBuyTab
        {
            get { return _tab == 1; }
            set { if (value) SetTab(1); }
        }

        public bool IsSellTab
        {
            get { return _tab == 2; }
            set { if (value) SetTab(2); }
        }

        public bool IsRampTab => _tab != 0;

        private void SetTab(int tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            Raise(nameof(IsSwapTab), nameof(IsBuyTab), nameof(IsSellTab), nameof(IsRampTab), nameof(RampTitle), nameof(RampHint));
        }

        public AssetItem From
        {
            get { return _from; }
            set
            {
                if (value == null || value == _from) return;
                if (value == _to) _to = _from;
                _from = value;
                OnPairChanged();
            }
        }

        public AssetItem To
        {
            get { return _to; }
            set
            {
                if (value == null || value == _to) return;
                if (value == _from) _from = _to;
                _to = value;
                OnPairChanged();
            }
        }

        private bool _useMax;

        public string AmountText
        {
            get { return _amountText; }
            set
            {
                if (!Set(ref _amountText, value ?? "")) return;
                _useMax = false;
                var ignored = QuoteSoonAsync();
            }
        }

        private decimal Amount
        {
            get
            {
                decimal a;
                return Formatters.TryParseAmount(AmountText, out a) ? a : 0;
            }
        }

        public string FromBalanceText => _from == null ? "" : Loc.T("Balance ") + (_main.HideBalances ? Formatters.Hidden : Formatters.Crypto(_from.Balance) + " " + _from.Symbol);
        public string ToBalanceText => _to == null ? "" : Loc.T("Balance ") + (_main.HideBalances ? Formatters.Hidden : Formatters.Crypto(_to.Balance) + " " + _to.Symbol);
        public string FromFiatText => _from == null || _from.Price == 0 || Amount == 0 ? "" : "≈ " + Formatters.Fiat(Amount * _from.Price, _main.Currency);
        public string ReceiveText => _quote == null ? "0" : Formatters.Crypto(_quote.ToAmount);
        public string ReceiveFiatText => _quote == null || _to == null || _to.Price == 0 ? "" : "≈ " + Formatters.Fiat(_quote.ToAmount * _to.Price, _main.Currency);
        public string RateText => _quote == null || _from == null || _to == null ? "" : "1 " + _from.Symbol + " ≈ " + Formatters.Crypto(_quote.Rate) + " " + _to.Symbol;
        public string MinText => _quote == null || !_quote.Min.HasValue || _from == null ? "" : Loc.F("Min {0}", Formatters.Crypto(_quote.Min.Value) + " " + _from.Symbol);
        public bool HasQuote => _quote != null;
        public string QuoteError => _quoteError;
        public bool HasQuoteError => !string.IsNullOrEmpty(_quoteError);
        public bool IsQuoting => _isQuoting;
        public bool IsSwapping => _isSwapping;
        public bool CanSwap => _quote != null && !_isQuoting && !_isSwapping && Amount > 0 && _from != null && _to != null && Amount <= _from.Balance;
        public bool HasSwaps => Swaps.Count > 0;

        private void OnPairChanged()
        {
            _quote = null;
            _quoteError = null;
            Raise(nameof(From), nameof(To), nameof(AmountText), nameof(FromBalanceText), nameof(ToBalanceText));
            RaiseQuote();
            var ignored = QuoteSoonAsync();
        }

        private void RaiseQuote()
        {
            Raise(nameof(ReceiveText), nameof(ReceiveFiatText), nameof(RateText), nameof(MinText), nameof(HasQuote), nameof(QuoteError),
                  nameof(HasQuoteError), nameof(IsQuoting), nameof(CanSwap), nameof(FromFiatText), nameof(IsSwapping));
            SwapCommand.Refresh();
        }

        public async Task QuoteSoonAsync(int delayMs = 600)
        {
            int version = ++_quoteVersion;
            _quote = null;
            _quoteError = null;
            if (Amount <= 0 || _from == null || _to == null)
            {
                _isQuoting = false;
                RaiseQuote();
                return;
            }
            _isQuoting = true;
            RaiseQuote();
            if (delayMs > 0) await Task.Delay(delayMs);
            if (version != _quoteVersion) return;
            try
            {
                var fromNet = await _main.Swap.NetworkCodeAsync(_from.Symbol, _from.Chain.Id);
                var toNet = await _main.Swap.NetworkCodeAsync(_to.Symbol, _to.Chain.Id);
                var quote = await _main.Swap.QuoteAsync(_from.Symbol, fromNet, _to.Symbol, toNet, Amount);
                if (version != _quoteVersion) return;
                _quote = quote;
                if (Amount > _from.Balance) _quoteError = Loc.F("Not enough {0} on {1}", _from.Symbol, _from.Network);
            }
            catch (SwapException e)
            {
                if (version == _quoteVersion) _quoteError = MainViewModel.Clean(e.Message);
            }
            finally
            {
                if (version == _quoteVersion)
                {
                    _isQuoting = false;
                    RaiseQuote();
                }
            }
        }

        private async Task SwapAsync()
        {
            var from = _from;
            var to = _to;
            var amount = Amount;
            var quote = _quote;
            if (from == null || to == null || quote == null || amount <= 0) return;
            if (_main.Wallet.IsWatchOnly)
            {
                _quoteError = Loc.T("This wallet can't sign swaps here yet. Swap from your hardware wallet app");
                RaiseQuote();
                return;
            }
            _isSwapping = true;
            RaiseQuote();
            try
            {
                var fromNet = await _main.Swap.NetworkCodeAsync(from.Symbol, from.Chain.Id);
                var toNet = await _main.Swap.NetworkCodeAsync(to.Symbol, to.Chain.Id);
                var order = await _main.Swap.CreateAsync(from.Symbol, fromNet, to.Symbol, toNet, amount, to.Chain.ReceiveAddress, from.Chain.Address);
                if (!from.Chain.IsValidAddress(order.DepositAddress))
                    throw new SwapException(Loc.F("The swap service returned an address that is not valid for {0}", from.Network));
                if (!string.IsNullOrEmpty(order.DepositExtraId))
                    throw new SwapException(Loc.T("This pair needs a memo, which Sillet does not support yet"));

                var useMax = _useMax && amount >= from.Balance;
                var prepared = await from.Chain.PrepareTransferAsync(from.Asset, order.DepositAddress, amount, useMax);
                if (useMax && prepared.Amount > 0 && prepared.Amount != amount)
                {
                    amount = prepared.Amount;
                    order = await _main.Swap.CreateAsync(from.Symbol, fromNet, to.Symbol, toNet, amount, to.Chain.ReceiveAddress, from.Chain.Address);
                    if (!from.Chain.IsValidAddress(order.DepositAddress))
                        throw new SwapException(Loc.F("The swap service returned an address that is not valid for {0}", from.Network));
                    prepared = await from.Chain.PrepareTransferAsync(from.Asset, order.DepositAddress, amount, true);
                }
                var fee = Formatters.Crypto(prepared.Fee) + " " + prepared.FeeAsset.Symbol;
                var message = Loc.F("Send {0} on {1} and receive about {2} on {3}. Network fee {4}. Exchanged by {5}",
                              Formatters.Crypto(prepared.Amount) + " " + from.Symbol, from.Network,
                              Formatters.Crypto(order.AmountTo > 0 ? order.AmountTo : quote.ToAmount) + " " + to.Symbol, to.Network, fee, SwapService.Provider);
                message += "\n\n" + Loc.F("Deposit address: {0}", order.DepositAddress) + "\n" + Loc.F("Order: {0}", order.Id) + "\n" +
                           Loc.F("{0} is a third party. It holds your funds until the swap is done, and Sillet cannot reverse it. Terms: {1}", SwapService.Provider, SwapService.TermsUrl);
                if (!await _main.ConfirmAsync(Loc.T("Confirm swap"), message, Loc.T("Swap")))
                    return;
                var result = await from.Chain.BroadcastAsync(prepared);
                _main.RecordSent(prepared, result);
                _main.Swaps.Add(new SwapRecord(order.Id, DateTime.UtcNow, from.Symbol, from.Chain.Id, from.Network, to.Symbol, to.Chain.Id, to.Network,
                    amount, order.AmountTo > 0 ? order.AmountTo : quote.ToAmount, order.DepositAddress, order.Status, result.TxId));
                LoadSwaps();
                AmountText = "";
                _main.ShowToast(Loc.T("Swap started. It usually takes 5 to 30 minutes"));
            }
            catch (Exception e) when (e is SwapException || e is WalletException || e is HttpRequestException || e is TaskCanceledException)
            {
                _quoteError = e is SwapException ? MainViewModel.Clean(e.Message) : MainViewModel.Friendly(e);
            }
            finally
            {
                _isSwapping = false;
                RaiseQuote();
            }
        }

        public void LoadSwaps()
        {
            var mine = new HashSet<string>(_main.Wallet.Chains.Select(c => c.Address).Where(a => a != null));
            Swaps.Clear();
            foreach (var r in _main.Swaps.Items)
            {
                var from = _main.AllAssets.FirstOrDefault(a => a.Symbol == r.FromSymbol && a.Chain.Id == r.FromChain);
                var to = _main.AllAssets.FirstOrDefault(a => a.Symbol == r.ToSymbol && a.Chain.Id == r.ToChain);
                if (from != null && !mine.Contains(from.Chain.Address)) continue;
                Swaps.Add(new SwapItem(r, from, to));
            }
            Raise(nameof(HasSwaps));
        }

        public async Task RefreshSwapsAsync()
        {
            foreach (var item in Swaps.Where(s => s.IsPending).ToList())
            {
                try
                {
                    var order = await _main.Swap.StatusAsync(item.Record.Id);
                    if (order.Status != item.Status)
                    {
                        item.Status = order.Status;
                        _main.Swaps.Update(item.Record.Id, order.Status);
                        if (item.IsDone) _main.ShowToast(Loc.F("Swap to {0} completed", item.Record.ToSymbol));
                    }
                }
                catch (SwapException)
                {
                }
            }
        }

        public CoinGroup RampCoin
        {
            get { return _rampCoin; }
            set
            {
                if (value == null || !Set(ref _rampCoin, value)) return;
                Raise(nameof(RampNetworks));
                RampNetwork = value.BestNetwork();
            }
        }

        public IReadOnlyList<AssetItem> RampNetworks => _rampCoin == null ? new AssetItem[0] : _rampCoin.Assets;

        public AssetItem RampNetwork
        {
            get { return _rampNetwork; }
            set
            {
                if (value == null) return;
                if (Set(ref _rampNetwork, value)) Raise(nameof(RampAddress), nameof(RampHint));
            }
        }

        public string RampAddress => _rampNetwork == null ? "" : _rampNetwork.Chain.ReceiveAddress ?? "";
        public string RampTitle => IsSellTab ? Loc.T("Sell crypto") : Loc.T("Buy crypto");

        public string RampHint
        {
            get
            {
                if (_rampNetwork == null) return "";
                return IsSellTab
                    ? Loc.F("The provider shows where to send your {0}. Send it from Send, on the network they ask for", _rampNetwork.Symbol)
                    : Loc.F("Your {0} address on {1} is copied when you open a provider. Choose the same network there", _rampNetwork.Symbol, _rampNetwork.Network);
            }
        }

        private void OpenProvider(object parameter)
        {
            var provider = parameter as RampProvider;
            if (provider == null) return;
            if (IsBuyTab && RampAddress.Length > 0)
            {
                _main.CopyText(RampAddress);
                _main.ShowToast(Loc.F("Address copied. Paste it in {0}", provider.Name));
            }
            _main.Ui.OpenUrl(IsSellTab ? provider.SellUrl : provider.BuyUrl);
        }
    }
}
