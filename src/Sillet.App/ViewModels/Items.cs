using System;
using System.Collections.Generic;
using System.Linq;
using Sillet.App.Services;
using Sillet.Core;

namespace Sillet.App.ViewModels
{
    internal static class Lookup
    {
        /// <summary>The wallet picture that fits the theme: white on dark themes, black on the light one.</summary>
        public static string WalletIcon = "walleticonwhite";

        public static string CoinId(string symbol)
        {
            string id;
            return symbol != null && MarketService.CoinIds.TryGetValue(symbol, out id) ? id : null;
        }

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "BTC", "Bitcoin" },
            { "ETH", "Ethereum" },
            { "USDT", "Tether" },
            { "USDC", "USD Coin" },
            { "SOL", "Solana" },
            { "BNB", "BNB" },
            { "TRX", "TRON" },
            { "POL", "Polygon" },
            { "AVAX", "Avalanche" },
            { "LTC", "Litecoin" },
            { "DOGE", "Dogecoin" },
            { "KAIA", "Kaia" },
            { "xDAI", "xDAI" },
            { "WLD", "Worldcoin" },
            { "FDUSD", "First Digital USD" },
            { "TRUMP", "Official Trump" },
            { "LINK", "Chainlink" },
            { "XRP", "XRP" },
        };

        public static readonly string[] CoinOrder = { "BTC", "ETH", "USDT", "USDC", "SOL", "BNB", "TRX", "LTC", "DOGE", "POL", "AVAX", "KAIA", "xDAI", "WLD", "FDUSD", "TRUMP", "LINK" };

        public static string CoinName(string symbol)
        {
            string name;
            return symbol != null && Names.TryGetValue(symbol, out name) ? name : symbol;
        }

        public static int CoinRank(string symbol)
        {
            int index = Array.IndexOf(CoinOrder, symbol);
            return index < 0 ? CoinOrder.Length : index;
        }

        public static List<CoinGroup> GroupByCoin(IEnumerable<AssetItem> assets)
        {
            return assets.GroupBy(a => a.Asset.IsCustom ? a.Symbol + "|" + a.Chain.Id + "|" + a.Asset.Contract : a.Symbol)
                .Select(g => new CoinGroup(g.First().Symbol, g.ToList()))
                .OrderBy(g => CoinRank(g.Symbol))
                .ToList();
        }

        public static string Initial(string symbol)
        {
            return string.IsNullOrEmpty(symbol) ? "?" : symbol.Substring(0, 1);
        }
    }

    public sealed class AssetItem : ObservableObject
    {
        private string _iconPath;
        private string _displayName;

        public AssetItem(IChain chain, Asset asset)
        {
            Chain = chain;
            Asset = asset;
            CoinId = asset.IsCustom ? null : Lookup.CoinId(asset.Symbol);
            _iconPath = CoinId == null ? null : IconCache.TryGet(CoinId);
            _displayName = asset.Name;
            BalanceText = "-";
            FiatText = "";
            PriceText = "";
            ChangeText = "";
            IsUp = true;
        }

        public IChain Chain { get; }
        public Asset Asset { get; }
        public string CoinId { get; }
        public string Symbol => Asset.Symbol;
        public string Network => Chain.Name;
        public string Initial => Lookup.Initial(Symbol);
        public bool IsToken => !Asset.IsNative;
        public bool IsCustom => Asset.IsCustom;

        public string DisplayName
        {
            get { return _displayName; }
            set { Set(ref _displayName, value); }
        }

        public string IconPath
        {
            get { return _iconPath; }
            set { Set(ref _iconPath, value); }
        }

        public decimal Balance { get; private set; }
        public bool BalanceLoaded { get; private set; }
        public string BalanceError { get; private set; }
        public decimal Price { get; private set; }
        public decimal? Change24h { get; private set; }
        public decimal FiatValue => Balance * Price;
        public bool HasBalance => Balance > 0;

        public string BalanceText { get; private set; }
        public string FiatText { get; private set; }
        public string PriceText { get; private set; }
        public string ChangeText { get; private set; }
        public bool IsUp { get; private set; }
        public bool HasChange => Change24h.HasValue;

        public void SetBalance(decimal? balance, string error)
        {
            if (balance.HasValue)
            {
                Balance = balance.Value;
                BalanceLoaded = true;
            }
            BalanceError = error;
        }

        public void SetMarket(decimal price, decimal? change24h, string name)
        {
            Price = price;
            Change24h = change24h;
            if (!string.IsNullOrEmpty(name)) DisplayName = name;
        }

        public void UpdateTexts(string currency, bool hidden)
        {
            if (hidden) BalanceText = Formatters.Hidden;
            else if (BalanceLoaded) BalanceText = Formatters.Crypto(Balance) + " " + Symbol;
            else BalanceText = BalanceError == null ? "…" : "offline";
            FiatText = hidden ? Formatters.Hidden : Price > 0 ? Formatters.Fiat(FiatValue, currency) : "";
            PriceText = Price > 0 ? Formatters.Fiat(Price, currency) : "";
            ChangeText = Formatters.Percent(Change24h);
            IsUp = (Change24h ?? 0) >= 0;
            Raise(nameof(Balance), nameof(HasBalance), nameof(FiatValue), nameof(BalanceText), nameof(FiatText),
                  nameof(PriceText), nameof(ChangeText), nameof(IsUp), nameof(HasChange));
        }
    }

    public sealed class CoinItem : ObservableObject
    {
        private string _iconPath;
        private MarketCoin _coin;

        public CoinItem(MarketCoin coin)
        {
            _coin = coin;
            _iconPath = IconCache.TryGet(coin.Id);
            PriceText = "";
        }

        public MarketCoin Coin
        {
            get { return _coin; }
            private set { Set(ref _coin, value); }
        }

        public string Id => Coin.Id;
        public string Symbol => Coin.Symbol;
        public string Name => Coin.Name;
        public string Initial => Lookup.Initial(Symbol);

        public string IconPath
        {
            get { return _iconPath; }
            set { Set(ref _iconPath, value); }
        }

        private bool _isFavorite;

        public bool IsFavorite
        {
            get { return _isFavorite; }
            set { Set(ref _isFavorite, value); }
        }

        public string PriceText { get; private set; }
        public string ChangeText => Formatters.Percent(Coin.Change24h);
        public bool IsUp => (Coin.Change24h ?? 0) >= 0;
        public double[] Sparkline => Coin.Sparkline7d;
        public bool SparklineUp => Sparkline.Length < 2 || Sparkline[Sparkline.Length - 1] >= Sparkline[0];

        public void Update(MarketCoin coin, string currency)
        {
            Coin = coin;
            PriceText = Formatters.Fiat(coin.Price, currency);
            Raise(nameof(Symbol), nameof(Name), nameof(PriceText), nameof(ChangeText), nameof(IsUp), nameof(Sparkline), nameof(SparklineUp));
        }
    }

    public sealed class NavItem : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly string _title;

        public NavItem(string key, string glyph, string title)
        {
            Key = key;
            Glyph = glyph;
            _title = title;
            Loc.Changed += () =>
            {
                var handler = PropertyChanged;
                if (handler != null) handler(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Title)));
            };
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Key { get; }
        public string Glyph { get; }
        public string Title => Loc.T(_title);
    }

    /// <summary>One small fact on the home page: a label, a value and, optionally, whether it is good or bad news.</summary>
    public sealed class StatItem : IEquatable<StatItem>
    {
        public StatItem(string label, string value, string tone = "")
        {
            Label = label;
            Value = value;
            Tone = tone;
        }

        public string Label { get; }
        public string Value { get; }
        public string Tone { get; }
        public bool IsUp => Tone == "up";
        public bool IsDown => Tone == "down";

        public bool Equals(StatItem other)
        {
            return other != null && Label == other.Label && Value == other.Value && Tone == other.Tone;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StatItem);
        }

        public override int GetHashCode()
        {
            return (Label ?? "").GetHashCode() ^ (Value ?? "").GetHashCode();
        }
    }

    public sealed class SliceItem : IEquatable<SliceItem>
    {
        public SliceItem(string label, double value, string color, string percentText)
        {
            Label = label;
            Value = value;
            Color = color;
            PercentText = percentText;
        }

        public string Label { get; }
        public double Value { get; }
        public string Color { get; }
        public string PercentText { get; }

        public bool Equals(SliceItem other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Label == other.Label && Value.Equals(other.Value) && Color == other.Color && PercentText == other.PercentText;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SliceItem);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Label == null ? 0 : Label.GetHashCode());
                hash = hash * 31 + Value.GetHashCode();
                hash = hash * 31 + (Color == null ? 0 : Color.GetHashCode());
                hash = hash * 31 + (PercentText == null ? 0 : PercentText.GetHashCode());
                return hash;
            }
        }
    }

    public sealed class WordItem
    {
        public WordItem(int number, string word)
        {
            Number = number;
            Word = word;
        }

        public int Number { get; }
        public string Word { get; }
    }

    public sealed class CoinGroup : ObservableObject
    {
        public CoinGroup(string symbol, IList<AssetItem> assets)
        {
            Symbol = symbol;
            Assets = assets.ToList();
            Name = Lookup.CoinName(symbol);
            BalanceText = "";
            FiatText = "";
        }

        public string Symbol { get; }
        public string Name { get; }
        public IReadOnlyList<AssetItem> Assets { get; }
        public AssetItem Primary => Assets[0];
        public string Initial => Lookup.Initial(Symbol);
        public int NetworkCount => Assets.Count;
        public string NetworksText => Assets.Count == 1 ? Assets[0].Network : Loc.F("{0} networks", Assets.Count);
        public decimal TotalBalance => Assets.Sum(a => a.Balance);
        public decimal TotalFiat => Assets.Sum(a => a.FiatValue);
        public bool HasBalance => Assets.Any(a => a.HasBalance);
        public string AmountText => Formatters.Crypto(TotalBalance);
        public string BalanceText { get; private set; }
        public string FiatText { get; private set; }

        public AssetItem BestNetwork()
        {
            return Assets.OrderByDescending(a => a.FiatValue).ThenByDescending(a => a.Balance).First();
        }

        public void UpdateTexts(string currency, bool hidden)
        {
            BalanceText = hidden ? Formatters.Hidden : HasBalance ? Formatters.Crypto(TotalBalance) + " " + Symbol : "";
            FiatText = hidden ? Formatters.Hidden : TotalFiat > 0 ? Formatters.Fiat(TotalFiat, currency) : "";
            Raise(nameof(BalanceText), nameof(FiatText), nameof(HasBalance), nameof(TotalBalance), nameof(TotalFiat));
        }
    }

    public sealed class NewsDot : ObservableObject
    {
        private bool _isActive;

        public NewsDot(int index)
        {
            Index = index;
        }

        public int Index { get; }

        public bool IsActive
        {
            get { return _isActive; }
            set { Set(ref _isActive, value); }
        }
    }

    public sealed class ThemeOption : ObservableObject
    {
        private bool _isSelected;

        public ThemeOption(ThemeDefinition theme)
        {
            Theme = theme;
        }

        public ThemeDefinition Theme { get; }
        public string Key => Theme.Key;
        public string Name => Theme.Name;
        public string Background => Theme.Background;
        public string Surface => Theme.Surface;
        public string Accent => Theme.Accent;
        public string Text => Theme.Text;

        public bool IsSelected
        {
            get { return _isSelected; }
            set { Set(ref _isSelected, value); }
        }
    }

    public sealed class NotificationItem
    {
        public NotificationItem(AppNotification notification, bool isNew, DateTime nowUtc)
        {
            Notification = notification;
            IsNew = isNew;
            TimeText = Ago(notification.TimeUtc, nowUtc);
        }

        public AppNotification Notification { get; }
        public string Title => Notification.Title;
        public string Message => Notification.Message;
        public bool HasMessage => !string.IsNullOrEmpty(Notification.Message);
        public bool IsNew { get; }
        public string TimeText { get; }
        public bool IsPayment => Notification.Kind == "payment";
        public bool IsNews => Notification.Kind == "news";
        public bool IsPrice => Notification.Kind.StartsWith("price", StringComparison.Ordinal) || Notification.Kind == "alert";
        public bool IsSecurity => Notification.Kind == "security" || Notification.Kind == "update";
        public bool IsOther => !IsPayment && !IsNews && !IsPrice && !IsSecurity;
        public bool IsPositive => IsPayment || Notification.Kind == "price_up";
        public bool IsNegative => Notification.Kind == "price_down";
        public bool IsWarning => IsSecurity;
        public string Tone => IsPositive ? "positive" : IsNegative ? "negative" : IsWarning ? "warning" : "info";

        public string ActionText
        {
            get
            {
                switch (Notification.Kind)
                {
                    case "payment": return Loc.T("See in Activity");
                    case "news": return (Notification.Target ?? "").StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Loc.T("Read more") : "";
                    case "update": return Loc.T("Download");
                    case "security": return Loc.T("Review");
                    case "price": case "price_up": case "price_down": case "alert": return Loc.T("Open Market");
                    default: return "";
                }
            }
        }

        public bool HasAction => ActionText.Length > 0;

        public string Glyph
        {
            get
            {
                switch (Notification.Kind)
                {
                    case "payment": return "\uE896";
                    case "news": return "\uE946";
                    case "price": case "price_up": case "price_down": return "\uEAFC";
                    case "update": return "\uE896";
                    case "alert": return "\uEA8F";
                    case "security": return "\uEA18";
                    default: return "\uEA8F";
                }
            }
        }

        public static string Ago(DateTime timeUtc, DateTime nowUtc)
        {
            var span = nowUtc - timeUtc;
            if (span.TotalMinutes < 1) return Loc.T("Just now");
            if (span.TotalMinutes < 60) return Loc.F("{0} min ago", (int)span.TotalMinutes);
            if (span.TotalHours < 24) return Loc.F("{0} h ago", (int)span.TotalHours);
            if (span.TotalDays < 7) return Loc.F("{0} d ago", (int)span.TotalDays);
            return timeUtc.ToLocalTime().ToString("MMM d", Loc.Instance.Culture);
        }
    }
}
