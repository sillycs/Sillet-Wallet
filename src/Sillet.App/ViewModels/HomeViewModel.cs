// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Sillet.App.Services;

namespace Sillet.App.ViewModels
{
    public sealed class HomeViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly List<NewsItem> _news = new List<NewsItem>();
        private int _newsIndex;
        private int _newsTick;
        private bool _isNewsLoading;

        public HomeViewModel(MainViewModel main)
        {
            _main = main;
            Assets = new ObservableCollection<AssetItem>();
            HomeCoins = Lookup.GroupByCoin(main.AllAssets);
            _homeCoin = HomeCoins.FirstOrDefault(c => c.Symbol == main.Settings.HomeCoin) ?? HomeCoins.FirstOrDefault();
            Slices = new ObservableCollection<SliceItem>();
            Overview = new ObservableCollection<StatItem>();
            VisibleAssets = new ObservableCollection<AssetItem>();
            Movers = new ObservableCollection<MoverItem>();
            ToggleCoinsCommand = new RelayCommand(() =>
            {
                CoinsExpanded = !CoinsExpanded;
                SyncVisibleAssets();
            });
            TotalText = "";
            ChangeText = "";
            IsUp = true;
            main.DataChanged += Rebuild;
            main.PropertyChanged += OnMainChanged;

            RefreshCommand = new AsyncCommand(main.RefreshAllAsync);
            ToggleHideCommand = new RelayCommand(() => main.HideBalances = !main.HideBalances);
            ToggleCurrencyCommand = new RelayCommand(() => { if (main.SettingsPage != null) main.SettingsPage.ToggleCurrencyPickerCommand.Execute(null); });
            ToggleEmptyCommand = new RelayCommand(() =>
            {
                main.Settings.ShowEmptyAssets = !main.Settings.ShowEmptyAssets;
                main.Settings.Save();
                Rebuild();
            });
            SendCommand = new RelayCommand(p =>
            {
                main.Navigate("send");
                var asset = p as AssetItem;
                if (asset != null && main.Send != null) main.Send.Preselect(asset);
            });
            HistoryCommand = new RelayCommand(p =>
            {
                var asset = p as AssetItem;
                if (asset != null) main.ShowAssetHistory(asset.Symbol, asset.Chain.Id);
            });
            ReceiveCommand = new RelayCommand(() => main.Navigate("receive"));
            TradeCommand = new RelayCommand(() => main.Navigate("trade"));
            VisibleNews = new ObservableCollection<NewsItem>();
            NewsDots = new ObservableCollection<NewsDot>();
            NextNewsCommand = new RelayCommand(() => ShowNews(_newsIndex + 1, true), () => _news.Count > 1);
            PreviousNewsCommand = new RelayCommand(() => ShowNews(_newsIndex - 1, true), () => _news.Count > 1);
            GoToNewsCommand = new RelayCommand(p =>
            {
                var dot = p as NewsDot;
                if (dot != null) ShowNews(dot.Index, true);
            });
            OpenNewsCommand = new RelayCommand(p =>
            {
                var item = p as NewsItem;
                if (item != null && item.HasUrl) _main.Ui.OpenUrl(item.Url);
            });
            RefreshNewsCommand = new AsyncCommand(LoadNewsAsync);
            Rebuild();
            var ignored = StartNewsAsync();
        }

        public ObservableCollection<NewsItem> VisibleNews { get; }
        public void PauseNews()
        {
            _newsTick++;
        }
        public ObservableCollection<NewsDot> NewsDots { get; }
        public RelayCommand NextNewsCommand { get; }
        public RelayCommand PreviousNewsCommand { get; }
        public RelayCommand GoToNewsCommand { get; }
        public RelayCommand OpenNewsCommand { get; }
        public AsyncCommand RefreshNewsCommand { get; }
        public bool HasNews => _news.Count > 0;
        public bool HasManyNews => _news.Count > 1;
        public bool ShowNewsEmpty => !HasNews && !IsNewsLoading;
        public string NewsCounter => _news.Count == 0 ? "" : (_newsIndex + 1) + " / " + _news.Count;

        public bool IsNewsLoading
        {
            get { return _isNewsLoading; }
            private set
            {
                if (Set(ref _isNewsLoading, value)) Raise(nameof(ShowNewsEmpty));
            }
        }

        public void SetNews(IEnumerable<NewsItem> items)
        {
            _news.Clear();
            _news.AddRange(items);
            NewsDots.Clear();
            for (int i = 0; i < _news.Count; i++) NewsDots.Add(new NewsDot(i));
            _newsIndex = -1;
            VisibleNews.Clear();
            ShowNews(0, false);
            NextNewsCommand.Refresh();
            PreviousNewsCommand.Refresh();
            Raise(nameof(HasNews), nameof(HasManyNews), nameof(ShowNewsEmpty));
        }

        private void ShowNews(int index, bool byUser)
        {
            if (_news.Count == 0)
            {
                VisibleNews.Clear();
                Raise(nameof(NewsCounter));
                return;
            }
            index = ((index % _news.Count) + _news.Count) % _news.Count;
            if (byUser) _newsTick++;
            if (index == _newsIndex && VisibleNews.Count == 1) return;
            _newsIndex = index;
            VisibleNews.Clear();
            VisibleNews.Add(_news[index]);
            foreach (var dot in NewsDots) dot.IsActive = dot.Index == index;
            Raise(nameof(NewsCounter));
        }

        private async Task LoadNewsAsync()
        {
            IsNewsLoading = true;
            try
            {
                if (_news.Count == 0)
                {
                    var known = _main.News.GetCached();
                    if (known.Count > 0) SetNews(known);
                }
                var latest = await _main.News.GetAsync();
                if (latest.Count > 0 || _news.Count == 0)
                {
                    var same = latest.Count == _news.Count && latest.Zip(_news, (a, b) => a.Id == b.Id && a.Title == b.Title && a.Description == b.Description && a.Url == b.Url).All(x => x);
                    if (!same) SetNews(latest);
                }
            }
            finally
            {
                IsNewsLoading = false;
            }
        }

        private async Task StartNewsAsync()
        {
            await LoadNewsAsync();
            while (_main.Home == this || _main.Home == null)
            {
                int tick = _newsTick;
                await Task.Delay(TimeSpan.FromSeconds(9));
                if (!_main.IsUnlocked || (_main.Home != null && _main.Home != this)) return;
                if (tick == _newsTick && _news.Count > 1 && ReferenceEquals(_main.CurrentPage, this)) ShowNews(_newsIndex + 1, false);
            }
        }

        public ObservableCollection<AssetItem> Assets { get; }
        public ObservableCollection<SliceItem> Slices { get; }
        public ObservableCollection<StatItem> Overview { get; }

        private static int CoinPage => Performance.Lite ? 8 : 16;
        private bool _coinsExpanded;

        public ObservableCollection<AssetItem> VisibleAssets { get; }
        public RelayCommand ToggleCoinsCommand { get; }
        public string CoinsCountText => Loc.F("{0} coins", Assets.Count);
        public bool HasMoreCoins => Assets.Count > CoinPage;
        public string CoinsToggleText => CoinsExpanded ? Loc.T("Show less") : Loc.T("Show more");

        public bool CoinsExpanded
        {
            get { return _coinsExpanded; }
            private set
            {
                if (Set(ref _coinsExpanded, value)) Raise(nameof(CoinsToggleText));
            }
        }

        private void SyncVisibleAssets()
        {
            var wanted = (CoinsExpanded ? Assets : Assets.Take(CoinPage)).ToList();
            if (!wanted.SequenceEqual(VisibleAssets))
            {
                VisibleAssets.Clear();
                foreach (var a in wanted) VisibleAssets.Add(a);
            }
            Raise(nameof(HasMoreCoins), nameof(CoinsToggleText), nameof(CoinsCountText));
        }

        public ObservableCollection<MoverItem> Movers { get; }
        public bool HasMovers => Movers.Count > 0;

        private void RebuildMovers()
        {
            var coins = (_main.MarketCoins ?? new MarketCoin[0]).Where(c => c.Change24h.HasValue && c.Price > 0 && (c.Rank == null || c.Rank <= 100)).ToList();
            var wanted = new List<MarketCoin>();
            wanted.AddRange(coins.OrderByDescending(c => c.Change24h.Value).Take(3));
            wanted.AddRange(coins.OrderBy(c => c.Change24h.Value).Where(c => !wanted.Contains(c)).Take(3));
            var current = Movers.Select(m => m.Id + m.PriceText + m.ChangeText).ToList();
            var items = wanted.Select(c => new MoverItem(c, _main.Currency)).ToList();
            if (current.SequenceEqual(items.Select(m => m.Id + m.PriceText + m.ChangeText))) return;
            Movers.Clear();
            foreach (var m in items)
            {
                Movers.Add(m);
                if (m.IconPath == null)
                {
                    var item = m;
                    var ignored = LoadMoverIconAsync(item);
                }
            }
            Raise(nameof(HasMovers));
        }

        private static async Task LoadMoverIconAsync(MoverItem item)
        {
            var path = await IconCache.GetAsync(item.Id, item.Coin.ImageUrl);
            if (path != null) item.IconPath = path;
        }
        public bool HasOverview => Overview.Count > 0;

        public double TotalValue { get; private set; }
        public string TotalText { get; private set; }
        public string ChangeText { get; private set; }
        public bool IsUp { get; private set; }
        public bool HasChange { get; private set; }
        public bool HasFunds { get; private set; }
        public bool IsEmpty => _main.LastUpdate.HasValue && Assets.Count == 0;
        public string LastUpdateText => _main.LastUpdate.HasValue ? _main.LastUpdate.Value.ToString("HH:mm") : "";
        public bool HideBalances => _main.HideBalances;
        public string Currency => _main.Currency;
        public string CurrencyLabel => _main.CurrencyLabel;
        public SettingsViewModel CurrencyPicker => _main.SettingsPage;
        public string CurrencyToggleText => Loc.T("Change currency");
        public bool IsRefreshing => _main.IsRefreshing;
        public static bool AlwaysShowAll { get; set; }

        public bool ShowEmptyAssets => _main.Settings.ShowEmptyAssets;
        public string ToggleEmptyText => ShowEmptyAssets ? Loc.T("With balance only") : Loc.T("Show all");

        public bool HasFearGreed => _main.FearGreed != null;
        public double FearGreedValue => _main.FearGreed != null ? _main.FearGreed.Value : 50;
        public string FearGreedLabel => _main.FearGreed != null ? _main.FearGreed.Label : "";
        public string FearGreedNumber => _main.FearGreed != null ? _main.FearGreed.Value.ToString() : "";

        public AsyncCommand RefreshCommand { get; }
        public RelayCommand ToggleHideCommand { get; }
        public RelayCommand ToggleCurrencyCommand { get; }
        public RelayCommand ToggleEmptyCommand { get; }
        public RelayCommand SendCommand { get; }
        public RelayCommand HistoryCommand { get; }
        public RelayCommand ReceiveCommand { get; }
        public RelayCommand TradeCommand { get; }

        private void OnMainChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsRefreshing)) Raise(nameof(IsRefreshing));
            if (e.PropertyName == nameof(MainViewModel.CurrencyLabel)) Raise(nameof(CurrencyLabel), nameof(Currency));
        }

        private bool IsSmall(AssetItem a)
        {
            if (!_main.HideSmallBalances || a.Price <= 0) return false;
            var threshold = _main.UsdToFiat(1m);
            return a.FiatValue < threshold;
        }

        public RelayCommand OpenAlternativeCommand => _main.OpenAlternativeCommand;

        private CoinGroup _homeCoin;
        public IReadOnlyList<CoinGroup> HomeCoins { get; }

        public CoinGroup HomeCoin
        {
            get { return _homeCoin; }
            set
            {
                if (value == null || !Set(ref _homeCoin, value)) return;
                _main.Settings.HomeCoin = value.Symbol;
                _main.Settings.Save();
                RaiseHomeCoin();
            }
        }

        public string HomeCoinAmount
        {
            get
            {
                if (_homeCoin == null) return "";
                if (HideBalances) return Formatters.Hidden;
                return Formatters.Crypto(_homeCoin.TotalBalance) + " " + _homeCoin.Symbol;
            }
        }

        public string HomeCoinFiat
        {
            get
            {
                if (_homeCoin == null || HideBalances) return "";
                var price = _homeCoin.Assets.Select(a => a.Price).FirstOrDefault(p => p > 0);
                return price > 0 ? Formatters.Fiat(_homeCoin.TotalFiat, Currency) : "";
            }
        }

        public string HomeCoinPrice
        {
            get
            {
                if (_homeCoin == null) return "";
                var asset = _homeCoin.Assets.FirstOrDefault(a => a.Price > 0);
                return asset == null ? "" : "1 " + _homeCoin.Symbol + " = " + Formatters.Fiat(asset.Price, Currency);
            }
        }

        public string HomeCoinNetworks => _homeCoin == null ? "" : _homeCoin.NetworkCount == 1 ? _homeCoin.Assets[0].Network : Loc.F("On {0} networks", _homeCoin.NetworkCount);

        private decimal? HomeCoinChange => _homeCoin == null ? null : _homeCoin.Assets.Where(a => a.Price > 0).Select(a => a.Change24h).FirstOrDefault(c => c.HasValue);
        public bool HasHomeCoinChange => HomeCoinChange.HasValue;
        public string HomeCoinChangeText => Formatters.Percent(HomeCoinChange);
        public bool HomeCoinUp => (HomeCoinChange ?? 0) >= 0;

        public string HomeCoinMeta => string.Join("  ·  ", new[] { HomeCoinPrice, HomeCoinNetworks }.Where(t => !string.IsNullOrEmpty(t)));

        private void RaiseHomeCoin()
        {
            Raise(nameof(HomeCoin), nameof(HomeCoinAmount), nameof(HomeCoinFiat), nameof(HomeCoinPrice), nameof(HomeCoinNetworks), nameof(HomeCoinMeta),
                  nameof(HasHomeCoinChange), nameof(HomeCoinChangeText), nameof(HomeCoinUp));
        }

        private IReadOnlyList<ChartPoint> _portfolio = new ChartPoint[0];

        public IReadOnlyList<ChartPoint> PortfolioPoints
        {
            get
            {
                var fresh = _main.PortfolioPoints;
                if (!fresh.Select(x => x.Value).SequenceEqual(_portfolio.Select(x => x.Value)) || !fresh.Select(x => x.Time).SequenceEqual(_portfolio.Select(x => x.Time)))
                    _portfolio = fresh;
                return _portfolio;
            }
        }
        public bool HasPortfolioHistory => PortfolioPoints.Count >= 2;
        public string PortfolioDaysText => PortfolioPoints.Count == 1 ? Loc.T("1 day recorded") : Loc.F("{0} days recorded", PortfolioPoints.Count);

        public string PortfolioChangeText
        {
            get
            {
                var p = PortfolioPoints;
                if (p.Count < 2 || p[0].Value == 0) return "";
                var pct = (decimal)((p[p.Count - 1].Value - p[0].Value) / p[0].Value * 100);
                return Loc.F("{0} since {1}", Formatters.Percent(pct), p[0].Time.ToString("MMM d", Loc.Instance.Culture));
            }
        }

        public bool PortfolioIsUp
        {
            get
            {
                var p = PortfolioPoints;
                return p.Count < 2 || p[p.Count - 1].Value >= p[0].Value;
            }
        }

        private static decimal PreviousValue(AssetItem a)
        {
            if (a.Change24h.HasValue && a.Change24h.Value > -100) return a.FiatValue / (1 + a.Change24h.Value / 100);
            return a.FiatValue;
        }

        private void RebuildOverview(IReadOnlyList<AssetItem> all, decimal total)
        {
            var stats = new List<StatItem>();
            var held = all.Where(a => a.HasBalance).ToList();
            if (held.Count > 0)
            {
                stats.Add(new StatItem(Loc.T("Coins held"), held.Select(a => a.Symbol).Distinct().Count().ToString(Formatters.Display)));
                stats.Add(new StatItem(Loc.T("Networks in use"), held.Select(a => a.Chain.Id).Distinct().Count().ToString(Formatters.Display)));
                var top = held.OrderByDescending(a => a.FiatValue).First();
                if (total > 0) stats.Add(new StatItem(Loc.T("Largest holding"), top.Symbol + "  " + (top.FiatValue / total * 100).ToString("0", Formatters.Display) + "%"));
                var moves = held.Where(a => a.Change24h.HasValue).ToList();
                if (moves.Count > 0)
                {
                    var best = moves.OrderByDescending(a => a.Change24h.Value).First();
                    var worst = moves.OrderBy(a => a.Change24h.Value).First();
                    stats.Add(new StatItem(Loc.T("Best today"), best.Symbol + "  " + Formatters.Percent(best.Change24h), best.Change24h.Value >= 0 ? "up" : "down"));
                    stats.Add(new StatItem(Loc.T("Weakest today"), worst.Symbol + "  " + Formatters.Percent(worst.Change24h), worst.Change24h.Value >= 0 ? "up" : "down"));
                }
            }
            var since = DateTime.UtcNow.AddDays(-30);
            var month = _main.History.Entries.Count(e => e.TimeUtc >= since);
            var sent = _main.History.Entries.Count(e => e.TimeUtc >= since && !e.Incoming);
            if (held.Count > 0 || month > 0) stats.Add(new StatItem(Loc.T("Transfers, 30 days"), month.ToString(Formatters.Display) + "  (" + sent.ToString(Formatters.Display) + " " + Loc.T("sent") + ")"));
            if (_main.LastUpdate.HasValue) stats.Add(new StatItem(Loc.T("Last update"), _main.LastUpdate.Value.ToString("HH:mm", Formatters.Display)));
            if (!stats.SequenceEqual(Overview))
            {
                Overview.Clear();
                foreach (var s in stats) Overview.Add(s);
                Raise(nameof(HasOverview));
            }
        }

        private void Rebuild()
        {
            var currency = _main.Currency;
            var all = _main.AllAssets;
            var ordered = all
                .Where(a => AlwaysShowAll || ShowEmptyAssets || (a.HasBalance && !IsSmall(a)))
                .OrderByDescending(a => a.FiatValue).ThenByDescending(a => a.HasBalance).ToList();

            if (!ordered.SequenceEqual(Assets))
            {
                Assets.Clear();
                foreach (var a in ordered) Assets.Add(a);
            }
            SyncVisibleAssets();
            RebuildMovers();

            decimal total = all.Sum(a => a.FiatValue);
            decimal previous = all.Sum(a => PreviousValue(a));
            HasFunds = all.Any(a => a.HasBalance);
            TotalValue = (double)total;
            TotalText = HideBalances ? Formatters.Hidden : Formatters.Fiat(total, currency);
            HasChange = previous > 0;
            var changePct = previous > 0 ? (total - previous) / previous * 100 : 0;
            var changeFiat = total - previous;
            IsUp = changePct >= 0;
            ChangeText = HideBalances
                ? Formatters.Percent(changePct)
                : (changeFiat >= 0 ? "+" : "-") + Formatters.Fiat(Math.Abs(changeFiat), currency) + "  (" + Formatters.Percent(changePct) + ")";

            var slices = new List<SliceItem>();
            if (total > 0)
            {
                var groups = all.Where(a => a.FiatValue > 0)
                    .GroupBy(a => a.Symbol)
                    .Select(g => new KeyValuePair<string, decimal>(g.Key, g.Sum(a => a.FiatValue)))
                    .OrderByDescending(g => g.Value).ToList();
                var top = groups.Take(5).ToList();
                var others = groups.Skip(5).Sum(g => g.Value);
                if (others > 0) top.Add(new KeyValuePair<string, decimal>(Loc.T("Others"), others));
                for (int i = 0; i < top.Count; i++)
                {
                    var pct = top[i].Value / total * 100;
                    var palette = _main.Palette;
                    slices.Add(new SliceItem(top[i].Key, (double)top[i].Value, palette[i % palette.Length],
                        pct.ToString(pct < 1 ? "0.0" : "0", Formatters.Display) + "%"));
                }
            }
            if (!slices.SequenceEqual(Slices))
            {
                Slices.Clear();
                foreach (var s in slices) Slices.Add(s);
            }

            Raise(nameof(TotalValue), nameof(TotalText), nameof(ChangeText), nameof(IsUp), nameof(HasChange), nameof(HasFunds), nameof(IsEmpty),
                  nameof(LastUpdateText), nameof(HideBalances), nameof(Currency), nameof(CurrencyLabel), nameof(ShowEmptyAssets), nameof(ToggleEmptyText),
                  nameof(HasFearGreed), nameof(FearGreedValue), nameof(FearGreedLabel), nameof(FearGreedNumber),
                  nameof(PortfolioPoints), nameof(HasPortfolioHistory), nameof(PortfolioDaysText), nameof(PortfolioChangeText), nameof(PortfolioIsUp));
            RaiseHomeCoin();
        }
    }
}
