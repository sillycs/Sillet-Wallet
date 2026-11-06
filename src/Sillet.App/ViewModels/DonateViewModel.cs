using System.Collections.Generic;
using System.Linq;
using Sillet.App.Services;

namespace Sillet.App.ViewModels
{
    public sealed class DonateOption : ObservableObject
    {
        private bool _isSelected;

        public DonateOption(string symbol, string name, string network, string chainId, string address, AssetItem asset)
        {
            Symbol = symbol;
            Name = name;
            Network = network;
            ChainId = chainId;
            Address = address;
            Asset = asset;
        }

        public string Symbol { get; }
        public string Name { get; }
        public string Network { get; }
        public string ChainId { get; }
        public string Address { get; }
        public AssetItem Asset { get; }
        public string IconPath => Asset == null ? null : Asset.IconPath;
        public string Initial => Lookup.Initial(Symbol);
        public string ShortAddress => Formatters.ShortAddress(Address);

        public bool IsSelected
        {
            get { return _isSelected; }
            set { Set(ref _isSelected, value); }
        }
    }

    public sealed class DonateAmount : ObservableObject
    {
        private bool _isSelected;

        public DonateAmount(decimal fiat, string label)
        {
            Fiat = fiat;
            Label = label;
        }

        public decimal Fiat { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { Set(ref _isSelected, value); }
        }
    }

    public sealed class DonateViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private DonateOption _selected;
        private bool _copied;
        private int _copyId;

        public DonateViewModel(MainViewModel main)
        {
            _main = main;
            Options = new List<DonateOption>
            {
                Make("BTC", "Bitcoin", "Bitcoin", "bitcoin", "14R3Re7w3pzd6N1wj4C2gNzx596eUH26Cj"),
                Make("ETH", "Ethereum", "Ethereum", "ethereum", "0xc3633a27A75FD4AE7ddEdbb01C5Ba8703bd8D4d5"),
                Make("SOL", "Solana", "Solana", "solana", "7CmdFembSJoAaEG7d7kFvemacRG9p6ZcYvEKwvJqw1a3"),
                Make("LTC", "Litecoin", "Litecoin", "litecoin", "ltc1q037cyj7yue7wqpw5hsndyr6g5j9g5g4xgngrkv"),
                Make("USDT", "Tether", "TRON (TRC20)", "tron", "TVUzoQVUDdp1YnXaEEjmphAph8FxxYWLhh"),
            };
            SelectCommand = new RelayCommand(p => Selected = p as DonateOption ?? Selected);
            CopyCommand = new RelayCommand(Copy);
            SendCommand = new RelayCommand(SendFromWallet);
            RefreshAmounts();
            SelectAmountCommand = new RelayCommand(p =>
            {
                var amount = p as DonateAmount;
                foreach (var a in Amounts) a.IsSelected = a == amount && !a.IsSelected;
                Raise(nameof(AmountHint), nameof(HasAmountHint), nameof(SendText));
            });
            Selected = Options[0];
        }

        public IReadOnlyList<DonateOption> Options { get; }
        public RelayCommand SelectCommand { get; }
        public RelayCommand CopyCommand { get; }
        public RelayCommand SendCommand { get; }
        public RelayCommand SelectAmountCommand { get; }
        public IReadOnlyList<DonateAmount> Amounts { get; private set; }

        public void RefreshAmounts()
        {
            Amounts = new[] { 2m, 5m, 10m, 25m }
                .Select(v => _main.UsdToFiat(v))
                .Select(v => new DonateAmount(v, Formatters.Fiat(v, _main.Currency))).ToList();
            Raise(nameof(Amounts));
        }

        private DonateAmount SelectedAmount => Amounts.FirstOrDefault(a => a.IsSelected);

        private decimal? CryptoAmount
        {
            get
            {
                var amount = SelectedAmount;
                if (amount == null || _selected == null || _selected.Asset == null || _selected.Asset.Price <= 0) return null;
                return System.Math.Round(amount.Fiat / _selected.Asset.Price, 8);
            }
        }

        public string AmountHint
        {
            get
            {
                var crypto = CryptoAmount;
                if (crypto.HasValue) return "≈ " + Formatters.Crypto(crypto.Value) + " " + _selected.Symbol;
                return SelectedAmount != null ? Loc.T("Prices are loading, the amount is filled in on the Send page") : "";
            }
        }

        public bool HasAmountHint => AmountHint.Length > 0;
        public string BalanceText => _selected == null || _selected.Asset == null ? "" : Loc.T("You have ") + _selected.Asset.BalanceText;
        public bool IsWatchOnly => _main.Wallet.IsWatchOnly;
        public string SendHint => IsWatchOnly ? Loc.T("Scan the QR with your hardware wallet app") : "";

        public DonateOption Selected
        {
            get { return _selected; }
            set
            {
                if (value == null || !Set(ref _selected, value)) return;
                foreach (var o in Options) o.IsSelected = o == value;
                _copyId++;
                Copied = false;
                Raise(nameof(Address), nameof(Title), nameof(Subtitle), nameof(CanSendFromWallet), nameof(SendText), nameof(WarningText),
                      nameof(AmountHint), nameof(HasAmountHint), nameof(BalanceText));
            }
        }

        public string Address => _selected == null ? "" : _selected.Address;
        public string Title => _selected == null ? "" : Loc.F("Donate {0}", _selected.Name);
        public string Subtitle => _selected == null ? "" : Loc.F("{0} on {1}", _selected.Symbol, _selected.Network);
        public string WarningText => _selected == null ? "" : Loc.F("Send only {0} on {1} to this address", _selected.Symbol, _selected.Network);
        public bool CanSendFromWallet => _selected != null && _selected.Asset != null && !_main.Wallet.IsWatchOnly;

        public string SendText
        {
            get
            {
                if (_selected == null) return Loc.T("Donate from Sillet");
                var amount = SelectedAmount;
                return amount == null ? Loc.F("Donate {0} from Sillet", _selected.Symbol) : Loc.F("Donate {0} in {1}", amount.Label, _selected.Symbol);
            }
        }

        public bool Copied
        {
            get { return _copied; }
            private set { Set(ref _copied, value); }
        }

        private DonateOption Make(string symbol, string name, string network, string chainId, string address)
        {
            var asset = _main.AllAssets.FirstOrDefault(a => a.Chain.Id == chainId && a.Symbol == symbol);
            return new DonateOption(symbol, name, network, chainId, address, asset);
        }

        private async void Copy()
        {
            if (_selected == null) return;
            _main.CopyText(_selected.Address);
            Copied = true;
            var id = ++_copyId;
            await System.Threading.Tasks.Task.Delay(1800);
            if (id == _copyId) Copied = false;
        }

        private void SendFromWallet()
        {
            if (!CanSendFromWallet) return;
            var option = _selected;
            _main.Navigate("send");
            if (_main.Send == null) return;
            var crypto = CryptoAmount;
            _main.Send.Preselect(option.Asset);
            _main.Send.ToAddress = option.Address;
            if (crypto.HasValue) _main.Send.AmountText = crypto.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
