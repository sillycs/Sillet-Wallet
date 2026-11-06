using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Sillet.App.Services;

namespace Sillet.App.ViewModels
{
    public sealed class MarketViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private CoinItem _selected;
        private int _days = 7;
        private IReadOnlyList<ChartPoint> _chart = new ChartPoint[0];
        private bool _isChartLoading;
        private string _chartError;
        private int _chartVersion;
        private string _convertInput = "1";
        private bool _fiatToCrypto;
        private string _lastCurrency;
        private string _searchText = "";
        private bool _filtering;
        private bool _alertAbove = true;
        private string _alertPriceText = "";

        public MarketViewModel(MainViewModel main)
        {
            _main = main;
            Coins = new ObservableCollection<CoinItem>();
            VisibleCoins = new ObservableCollection<CoinItem>();
            main.DataChanged += Sync;
            RetryChartCommand = new AsyncCommand(LoadChartAsync);
            SwapConvertCommand = new RelayCommand(() => FiatToCrypto = !FiatToCrypto);
            Alerts = new ObservableCollection<PriceAlert>();
            AlertAbove = true;
            AlertPriceText = "";
            HistoryCommand = new RelayCommand(() =>
            {
                if (SelectedCoin != null) _main.ShowAssetHistory(SelectedCoin.Symbol, null);
            });
            ToggleFavoriteCommand = new RelayCommand(p =>
            {
                var coin = p as CoinItem ?? SelectedCoin;
                if (coin == null) return;
                coin.IsFavorite = !coin.IsFavorite;
                if (coin.IsFavorite) Sounds.Play(Sounds.Favorite);
                var ids = Coins.Where(c => c.IsFavorite).Select(c => c.Id);
                _main.Settings.FavoriteCoins = string.Join(",", ids);
                _main.Settings.Save();
                ApplyFilter();
                Raise(nameof(IsSelectedFavorite));
            });
            AddAlertCommand = new RelayCommand(AddAlert);
            RemoveAlertCommand = new RelayCommand(p =>
            {
                var alert = p as PriceAlert;
                if (alert == null) return;
                _main.Alerts.Remove(alert);
                ReloadAlerts();
            });
            OpenPredictionCommand = new RelayCommand(p =>
            {
                var item = p as PredictionItem;
                if (item != null) _main.Ui.OpenUrl(item.Url);
            });
            RefreshPredictionsCommand = new AsyncCommand(() => LoadPredictionsAsync(true));
            Sync();
            var firstLoad = LoadPredictionsAsync(false);
        }

        private readonly PredictionService _predictions = new PredictionService();
        private bool _showPredictions, _predictionsLoading, _reload;
        private string _predictionsError;
        private string _category = "trending";

        public string PredictionCategory
        {
            get { return _category; }
            set
            {
                if (string.IsNullOrEmpty(value) || value == _category) return;
                _category = value;
                Raise(nameof(PredictionCategory), nameof(IsTrending), nameof(IsNewest), nameof(IsSports), nameof(IsCrypto), nameof(IsPolitics));
                Predictions.Clear();
                Raise(nameof(HasPredictions));
                var ignored = LoadPredictionsAsync(false);
            }
        }

        public bool IsTrending { get { return _category == "trending"; } set { if (value) PredictionCategory = "trending"; } }
        public bool IsNewest { get { return _category == "new"; } set { if (value) PredictionCategory = "new"; } }
        public bool IsSports { get { return _category == "sports"; } set { if (value) PredictionCategory = "sports"; } }
        public bool IsCrypto { get { return _category == "crypto"; } set { if (value) PredictionCategory = "crypto"; } }
        public bool IsPolitics { get { return _category == "politics"; } set { if (value) PredictionCategory = "politics"; } }

        public ObservableCollection<PredictionItem> Predictions { get; } = new ObservableCollection<PredictionItem>();
        public RelayCommand OpenPredictionCommand { get; }
        public AsyncCommand RefreshPredictionsCommand { get; }
        public bool HasPredictions { get { return Predictions.Count > 0; } }
        public bool HasPredictionsError { get { return _predictionsError != null; } }
        public string PredictionsError { get { return _predictionsError; } }
        public bool IsPredictionsLoading { get { return _predictionsLoading; } }

        public bool ShowPredictions
        {
            get { return _showPredictions; }
            set { if (Set(ref _showPredictions, value)) Raise(nameof(ShowCoins)); }
        }

        public bool ShowCoins { get { return !_showPredictions; } set { ShowPredictions = !value; } }

        private async Task LoadPredictionsAsync(bool force)
        {
            if (_predictionsLoading) { _reload = true; return; }
            _predictionsLoading = true;
            _predictionsError = null;
            Raise(nameof(IsPredictionsLoading), nameof(HasPredictionsError), nameof(PredictionsError));
            try
            {
                var events = await _predictions.GetAsync(_category, force);
                Predictions.Clear();
                foreach (var e in events) Predictions.Add(new PredictionItem(e, url => _main.Ui.OpenUrl(url)));
            }
            catch (System.Exception e) when (e is System.Net.Http.HttpRequestException || e is System.FormatException || e is System.InvalidOperationException || e is TaskCanceledException || e is System.Collections.Generic.KeyNotFoundException)
            {
                _predictionsError = Loc.T("Could not load predictions");
            }
            _predictionsLoading = false;
            Raise(nameof(IsPredictionsLoading), nameof(HasPredictions), nameof(HasPredictionsError), nameof(PredictionsError));
            if (_reload)
            {
                _reload = false;
                await LoadPredictionsAsync(false);
            }
        }

        public ObservableCollection<CoinItem> Coins { get; }
        public ObservableCollection<CoinItem> VisibleCoins { get; }
        public RelayCommand HistoryCommand { get; }
        public RelayCommand ToggleFavoriteCommand { get; }
        public RelayCommand AddAlertCommand { get; }
        public RelayCommand RemoveAlertCommand { get; }
        public ObservableCollection<PriceAlert> Alerts { get; }
        public bool HasAlerts => Alerts.Count > 0;
        public bool IsSelectedFavorite => SelectedCoin != null && SelectedCoin.IsFavorite;
        public event System.Action FocusSearchRequested;

        private bool _pendingSearchFocus;

        public void RequestSearchFocus()
        {
            _pendingSearchFocus = true;
            var handler = FocusSearchRequested;
            if (handler != null) handler();
        }

        public bool ConsumeSearchFocus()
        {
            var pending = _pendingSearchFocus;
            _pendingSearchFocus = false;
            return pending;
        }

        public bool AlertAbove
        {
            get { return _alertAbove; }
            set
            {
                if (Set(ref _alertAbove, value)) Raise(nameof(AlertBelow));
            }
        }

        public bool AlertBelow
        {
            get { return !_alertAbove; }
            set { AlertAbove = !value; }
        }

        public string AlertPriceText
        {
            get { return _alertPriceText; }
            set { Set(ref _alertPriceText, value ?? ""); }
        }

        private void AddAlert()
        {
            decimal price;
            if (SelectedCoin == null) return;
            if (!Formatters.TryParseAmount(AlertPriceText, out price) || price <= 0)
            {
                _main.ShowToast(Loc.T("Type a price for the alert"), true);
                return;
            }
            _main.Alerts.Add(SelectedCoin.Id, SelectedCoin.Symbol, AlertAbove, price, _main.Currency);
            AlertPriceText = "";
            ReloadAlerts();
            _main.ShowToast(Loc.T("Alert saved"));
        }

        public void ReloadAlerts()
        {
            Alerts.Clear();
            if (SelectedCoin != null)
                foreach (var a in _main.Alerts.Items.Where(a => a.CoinId == SelectedCoin.Id)) Alerts.Add(a);
            Raise(nameof(HasAlerts));
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (Set(ref _searchText, value ?? "")) ApplyFilter();
            }
        }

        public bool HasNoMatches => Coins.Count > 0 && VisibleCoins.Count == 0;

        private void ApplyFilter()
        {
            var q = (_searchText ?? "").Trim();
            var filtered = Coins.Where(c => q.Length == 0
                || c.Symbol.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0
                || c.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(c => c.IsFavorite).ToList();
            if (!filtered.SequenceEqual(VisibleCoins))
            {
                var keep = _selected;
                _filtering = true;
                VisibleCoins.Clear();
                foreach (var c in filtered) VisibleCoins.Add(c);
                _filtering = false;
                if (keep != null) Raise(nameof(SelectedCoin));
            }
            Raise(nameof(HasNoMatches));
        }

        public CoinItem SelectedCoin
        {
            get { return _selected; }
            set
            {
                if (value == null && (_filtering || _selected != null)) return;
                if (!Set(ref _selected, value)) return;
                RaiseDetails();
                ReloadAlerts();
                Raise(nameof(IsSelectedFavorite));
                var ignored = LoadChartAsync();
            }
        }

        public bool Range1
        {
            get { return _days == 1; }
            set { if (value) SetDays(1); }
        }

        public bool Range7
        {
            get { return _days == 7; }
            set { if (value) SetDays(7); }
        }

        public bool Range30
        {
            get { return _days == 30; }
            set { if (value) SetDays(30); }
        }

        public bool Range365
        {
            get { return _days == 365; }
            set { if (value) SetDays(365); }
        }

        public string RangeLabel
        {
            get
            {
                switch (_days)
                {
                    case 1: return Loc.T("24 h");
                    case 7: return Loc.T("7 days");
                    case 30: return Loc.T("30 days");
                    default: return Loc.T("1 year");
                }
            }
        }

        private void SetDays(int days)
        {
            if (_days == days) return;
            _days = days;
            Raise(nameof(Range1), nameof(Range7), nameof(Range30), nameof(Range365), nameof(RangeLabel));
            var ignored = LoadChartAsync();
        }

        public IReadOnlyList<ChartPoint> ChartPoints
        {
            get { return _chart; }
            private set { Set(ref _chart, value); }
        }

        public bool IsChartLoading
        {
            get { return _isChartLoading; }
            private set { Set(ref _isChartLoading, value); }
        }

        public string ChartError
        {
            get { return _chartError; }
            private set
            {
                Set(ref _chartError, value);
                Raise(nameof(HasChartError));
            }
        }

        public bool HasChartError => ChartError != null;
        public bool ChartIsUp => ChartPoints.Count < 2 || ChartPoints[ChartPoints.Count - 1].Value >= ChartPoints[0].Value;

        public string ChartChangeText
        {
            get
            {
                if (ChartPoints.Count < 2 || ChartPoints[0].Value == 0) return "";
                var pct = (decimal)((ChartPoints[ChartPoints.Count - 1].Value - ChartPoints[0].Value) / ChartPoints[0].Value * 100);
                return Formatters.Percent(pct) + "  ·  " + RangeLabel;
            }
        }

        public string Currency => _main.Currency;
        public AsyncCommand RetryChartCommand { get; }

        private MarketCoin Coin => SelectedCoin == null ? null : SelectedCoin.Coin;

        private string FiatOrDash(decimal? v)
        {
            return v.HasValue ? Formatters.Fiat(v.Value, Currency) : "-";
        }

        private string CompactOrDash(decimal? v)
        {
            return v.HasValue ? Formatters.Compact(v.Value, Currency) : "-";
        }

        public string PriceText => Coin == null ? "" : Formatters.Fiat(Coin.Price, Currency);
        public double PriceValue => Coin == null ? 0 : (double)Coin.Price;
        public string Change24hText => Formatters.Percent(Coin == null ? null : Coin.Change24h);
        public bool IsUp24h => Coin == null || (Coin.Change24h ?? 0) >= 0;
        public string High24hText => Coin == null ? "-" : FiatOrDash(Coin.High24h);
        public string Low24hText => Coin == null ? "-" : FiatOrDash(Coin.Low24h);
        public string MarketCapText => Coin == null ? "-" : CompactOrDash(Coin.MarketCap);
        public string VolumeText => Coin == null ? "-" : CompactOrDash(Coin.Volume24h);
        public string RankText => Coin != null && Coin.Rank.HasValue ? "#" + Coin.Rank.Value : "-";
        public string AthText => Coin == null ? "-" : FiatOrDash(Coin.Ath);
        public string Change7dText => Formatters.Percent(Coin == null ? null : Coin.Change7d);
        public bool IsUp7d => Coin == null || (Coin.Change7d ?? 0) >= 0;

        public string FromAthText
        {
            get
            {
                var coin = Coin;
                if (coin == null || !coin.Ath.HasValue || coin.Ath.Value == 0) return "";
                var ath = coin.Ath.Value;
                var pct = (coin.Price - ath) / ath * 100;
                return pct >= -0.5m ? Loc.T("At all-time high") : Loc.F("{0} from the high", Formatters.Percent(pct));
            }
        }

        public string ConvertInput
        {
            get { return _convertInput; }
            set
            {
                if (Set(ref _convertInput, value)) Raise(nameof(ConvertOutput));
            }
        }

        public bool FiatToCrypto
        {
            get { return _fiatToCrypto; }
            private set
            {
                if (Set(ref _fiatToCrypto, value)) Raise(nameof(ConvertOutput), nameof(ConvertFromLabel), nameof(ConvertToLabel));
            }
        }

        private string SelectedSymbol => SelectedCoin == null ? "" : SelectedCoin.Symbol;
        public string ConvertFromLabel => FiatToCrypto ? _main.CurrencyLabel : SelectedSymbol;
        public string ConvertToLabel => FiatToCrypto ? SelectedSymbol : _main.CurrencyLabel;

        public string ConvertOutput
        {
            get
            {
                var coin = Coin;
                decimal amount;
                if (coin == null || coin.Price == 0 || !Formatters.TryParseAmount(ConvertInput, out amount)) return "-";
                return FiatToCrypto
                    ? Formatters.Crypto(amount / coin.Price) + " " + coin.Symbol
                    : Formatters.Fiat(amount * coin.Price, Currency);
            }
        }

        public RelayCommand SwapConvertCommand { get; }

        public string Attribution => _main.Market.IsStale ? Loc.T("Data: CoinGecko (offline)") : Loc.T("Data: CoinGecko");

        public void SetIcon(string coinId, string path)
        {
            foreach (var c in Coins.Where(c => c.Id == coinId)) c.IconPath = path;
        }

        private void Sync()
        {
            var currency = _main.Currency;
            foreach (var coin in _main.MarketCoins)
            {
                var item = Coins.FirstOrDefault(c => c.Id == coin.Id);
                if (item == null)
                {
                    item = new CoinItem(coin);
                    item.IsFavorite = ("," + (_main.Settings.FavoriteCoins ?? "") + ",").Contains("," + coin.Id + ",");
                    Coins.Add(item);
                }
                item.Update(coin, currency);
            }
            ApplyFilter();
            bool currencyChanged = _lastCurrency != null && _lastCurrency != currency;
            _lastCurrency = currency;
            if (SelectedCoin == null && Coins.Count > 0)
            {
                SelectedCoin = Coins[0];
            }
            else
            {
                RaiseDetails();
                if (currencyChanged)
                {
                    var ignored = LoadChartAsync();
                }
            }
        }

        private void RaiseDetails()
        {
            Raise(nameof(PriceText), nameof(PriceValue), nameof(Change24hText), nameof(IsUp24h), nameof(High24hText), nameof(Low24hText),
                nameof(MarketCapText), nameof(VolumeText), nameof(RankText), nameof(AthText), nameof(Change7dText), nameof(IsUp7d),
                nameof(FromAthText), nameof(ConvertOutput), nameof(ConvertFromLabel), nameof(ConvertToLabel), nameof(Currency), nameof(Attribution));
        }

        private async Task LoadChartAsync()
        {
            if (SelectedCoin == null) return;
            int version = ++_chartVersion;
            IsChartLoading = true;
            ChartError = null;
            try
            {
                var points = await _main.Market.GetChartAsync(SelectedCoin.Id, Currency, _days);
                if (version != _chartVersion) return;
                ChartPoints = points;
            }
            catch (MarketException e)
            {
                if (version == _chartVersion) ChartError = MainViewModel.Clean(e.Message);
            }
            finally
            {
                if (version == _chartVersion) IsChartLoading = false;
                Raise(nameof(ChartIsUp), nameof(ChartChangeText));
            }
        }
    }
}

namespace Sillet.App.ViewModels
{
    public sealed class PredictionRow
    {
        public PredictionRow(string label, int percent, System.Action open)
        {
            OpenCommand = new RelayCommand(open);
            Label = label == "Chance" ? Loc.T("Chance") : label;
            PercentText = percent + "%";
        }

        public RelayCommand OpenCommand { get; private set; }
        public string Label { get; private set; }
        public string PercentText { get; private set; }
    }

    public sealed class PredictionItem
    {
        private readonly PredictionEvent _event;

        public PredictionItem(PredictionEvent e, System.Action<string> open)
        {
            _event = e;
            Rows = e.Options.Select(o => new PredictionRow(o.Label, o.Percent, () => open(e.Url))).ToList();
        }

        public string Title { get { return _event.Title; } }
        public string Url { get { return _event.Url; } }
        public System.Collections.Generic.IReadOnlyList<PredictionRow> Rows { get; private set; }
        public bool HasMore { get { return _event.More > 0; } }
        public string MoreText { get { return Loc.F("+{0} outcomes", _event.More); } }
        public string VolumeText { get { return _event.Volume > 0 ? Loc.F("{0} Vol.", Compact(_event.Volume)) : ""; } }

        private static string Compact(decimal v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (v >= 1000000m) return "$" + (v / 1000000m).ToString("0.#", inv) + "M";
            if (v >= 1000m) return "$" + (v / 1000m).ToString("0.#", inv) + "K";
            return "$" + v.ToString("0", inv);
        }
    }
}
