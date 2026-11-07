// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public sealed class MarketCoin
    {
        public MarketCoin(string id, string symbol, string name, string imageUrl, decimal price, decimal? change24h, decimal? change7d,
            decimal? marketCap, decimal? volume24h, decimal? high24h, decimal? low24h, int? rank, decimal? ath, double[] sparkline7d)
        {
            Id = id;
            Symbol = symbol;
            Name = name;
            ImageUrl = imageUrl;
            Price = price;
            Change24h = change24h;
            Change7d = change7d;
            MarketCap = marketCap;
            Volume24h = volume24h;
            High24h = high24h;
            Low24h = low24h;
            Rank = rank;
            Ath = ath;
            Sparkline7d = sparkline7d;
        }

        public string Id { get; }
        public string Symbol { get; }
        public string Name { get; }
        public string ImageUrl { get; }
        public decimal Price { get; }
        public decimal? Change24h { get; }
        public decimal? Change7d { get; }
        public decimal? MarketCap { get; }
        public decimal? Volume24h { get; }
        public decimal? High24h { get; }
        public decimal? Low24h { get; }
        public int? Rank { get; }
        public decimal? Ath { get; }
        public double[] Sparkline7d { get; }

        public MarketCoin Scaled(decimal rate)
        {
            Func<decimal?, decimal?> f = v => v.HasValue ? v.Value * rate : (decimal?)null;
            var factor = (double)rate;
            return new MarketCoin(Id, Symbol, Name, ImageUrl, Price * rate, Change24h, Change7d, f(MarketCap), f(Volume24h), f(High24h), f(Low24h),
                Rank, f(Ath), Sparkline7d.Select(p => p * factor).ToArray());
        }
    }

    public struct ChartPoint
    {
        public ChartPoint(DateTime time, double value)
        {
            Time = time;
            Value = value;
        }

        public DateTime Time { get; }
        public double Value { get; }
    }

    public sealed class FearGreed
    {
        public FearGreed(int value, string label)
        {
            Value = value;
            Label = label;
        }

        public int Value { get; }
        public string Label { get; }
    }

    public sealed class MarketException : Exception
    {
        public MarketException(string message) : base(message)
        {
        }
    }

    public sealed class MarketService
    {
        private const string Api = "https://api.coingecko.com/api/v3";

        public static readonly IReadOnlyDictionary<string, string> CoinIds = new Dictionary<string, string>
        {
            { "BTC", "bitcoin" },
            { "ETH", "ethereum" },
            { "USDT", "tether" },
            { "USDC", "usd-coin" },
            { "SOL", "solana" },
            { "BNB", "binancecoin" },
            { "TRX", "tron" },
            { "POL", "polygon-ecosystem-token" },
            { "AVAX", "avalanche-2" },
            { "LTC", "litecoin" },
            { "DOGE", "dogecoin" },
            { "KAIA", "kaia" },
            { "xDAI", "xdai" },
            { "WLD", "worldcoin-wld" },
            { "FDUSD", "first-digital-usd" },
            { "TRUMP", "official-trump" },
            { "LINK", "chainlink" },
            { "XRP", "ripple" },
            { "BCH", "bitcoin-cash" },
            { "TON", "the-open-network" },
            { "SUI", "sui" },
            { "ATOM", "cosmos" },
        };

        private readonly HttpClient _http;
        private readonly Dictionary<string, CachedChart> _charts = new Dictionary<string, CachedChart>();
        private CachedMarkets _markets;

        public MarketService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/1.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public bool IsStale { get; private set; }

        public async Task<IReadOnlyList<MarketCoin>> GetMarketsAsync(string currency, bool force = false, CancellationToken ct = default(CancellationToken))
        {
            currency = currency.ToLowerInvariant();
            var m = _markets;
            if (!force && m != null && m.Currency == currency && DateTime.UtcNow - m.At < TimeSpan.FromSeconds(60))
                return m.Coins;

            await EnsureRatesAsync(ct);
            var info = Currencies.Find(currency);
            var convert = info != null && !info.Direct;
            var source = convert ? "usd" : currency;
            var url = Api + "/coins/markets?vs_currency=" + source + "&ids=" + string.Join(",", CoinIds.Values) +
                      "&order=market_cap_desc&sparkline=true&price_change_percentage=24h,7d&precision=full";
            var cacheFile = Path.Combine(AppSettings.DataFolder, "cache", "markets-" + source + ".json");
            string json;
            try
            {
                json = await GetStringAsync(url, ct);
                TryWriteCache(cacheFile, json);
                IsStale = false;
            }
            catch (MarketException) when (File.Exists(cacheFile))
            {
                json = File.ReadAllText(cacheFile);
                IsStale = true;
            }

            IReadOnlyList<MarketCoin> coins;
            try
            {
                coins = ParseMarkets(json);
                if (convert) coins = coins.Select(c => c.Scaled(info.PerUsd)).ToList();
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is KeyNotFoundException)
            {
                throw new MarketException(Loc.T("The price service returned something unexpected"));
            }
            _markets = new CachedMarkets(DateTime.UtcNow, currency, coins);
            return coins;
        }

        private DateTime _ratesAt = DateTime.MinValue;

        public async Task EnsureRatesAsync(CancellationToken ct = default(CancellationToken))
        {
            if (DateTime.UtcNow - _ratesAt < TimeSpan.FromHours(6)) return;
            var cacheFile = Path.Combine(AppSettings.DataFolder, "cache", "rates.json");
            string json = null;
            try
            {
                json = await GetStringAsync("https://open.er-api.com/v6/latest/USD", ct);
                TryWriteCache(cacheFile, json);
            }
            catch (MarketException)
            {
                try
                {
                    if (File.Exists(cacheFile)) json = File.ReadAllText(cacheFile);
                }
                catch (IOException)
                {
                }
            }
            _ratesAt = DateTime.UtcNow - (json == null ? TimeSpan.FromHours(5.5) : TimeSpan.Zero);
            if (json == null) return;
            try
            {
                var rates = JsonValue.Parse(json)["rates"];
                var found = new Dictionary<string, decimal>();
                foreach (var c in Currencies.All)
                {
                    JsonValue v;
                    if (rates.TryGet(c.Code.ToUpperInvariant(), out v) && v.IsNumber) found[c.Code] = (decimal)v.AsDouble();
                }
                Currencies.SetRates(found);
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is KeyNotFoundException)
            {
            }
        }

        private static void TryWriteCache(string cacheFile, string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                File.WriteAllText(cacheFile, json);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        public static IReadOnlyList<MarketCoin> ParseMarkets(string json)
        {
            var root = JsonValue.Parse(json);
            var list = new List<MarketCoin>();
            foreach (var c in root.Items)
            {
                var spark = new double[0];
                JsonValue s, prices;
                if (c.TryGet("sparkline_in_7d", out s) && s.IsObject && s.TryGet("price", out prices) && prices.IsArray)
                    spark = prices.Items.Where(p => p.IsNumber).Select(p => p.AsDouble()).ToArray();

                JsonValue r;
                int? rank = null;
                if (c.TryGet("market_cap_rank", out r) && r.IsNumber) rank = r.AsInt();

                list.Add(new MarketCoin(
                    c["id"].AsString(),
                    (c["symbol"].AsString() ?? "").ToUpperInvariant(),
                    c["name"].AsString() ?? "",
                    Str(c, "image"),
                    Dec(c, "current_price") ?? 0,
                    Dec(c, "price_change_percentage_24h_in_currency") ?? Dec(c, "price_change_percentage_24h"),
                    Dec(c, "price_change_percentage_7d_in_currency"),
                    Dec(c, "market_cap"), Dec(c, "total_volume"), Dec(c, "high_24h"), Dec(c, "low_24h"),
                    rank,
                    Dec(c, "ath"),
                    spark));
            }
            return list;
        }

        public async Task<IReadOnlyList<ChartPoint>> GetChartAsync(string coinId, string currency, int days, CancellationToken ct = default(CancellationToken))
        {
            var key = coinId + "|" + currency + "|" + days;
            CachedChart cached;
            if (_charts.TryGetValue(key, out cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5))
                return cached.Points;

            await EnsureRatesAsync(ct);
            var info = Currencies.Find(currency);
            var convert = info != null && !info.Direct;
            var json = await GetStringAsync(Api + "/coins/" + coinId + "/market_chart?vs_currency=" + (convert ? "usd" : currency.ToLowerInvariant()) + "&days=" + days, ct);
            IReadOnlyList<ChartPoint> points;
            try
            {
                points = ParseChart(json);
                if (convert)
                {
                    var rate = (double)info.PerUsd;
                    points = points.Select(p => new ChartPoint(p.Time, p.Value * rate)).ToList();
                }
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is KeyNotFoundException)
            {
                throw new MarketException(Loc.T("The price service returned something unexpected"));
            }
            _charts[key] = new CachedChart(DateTime.UtcNow, points);
            return points;
        }

        public static IReadOnlyList<ChartPoint> ParseChart(string json)
        {
            var root = JsonValue.Parse(json);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return root["prices"].Items
                .Where(p => p.IsArray && p.Count >= 2 && p[0].IsNumber && p[1].IsNumber)
                .Select(p => new ChartPoint(epoch.AddMilliseconds(p[0].AsDouble()).ToLocalTime(), p[1].AsDouble()))
                .ToList();
        }

        public async Task<FearGreed> GetFearGreedAsync(CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var json = await GetStringAsync("https://api.alternative.me/fng/?limit=1", ct);
                var item = JsonValue.Parse(json)["data"][0];
                int value = int.Parse(item["value"].AsString(), CultureInfo.InvariantCulture);
                return new FearGreed(value, FearGreedLabel(value));
            }
            catch (Exception e) when (e is MarketException || e is KeyNotFoundException || e is ArgumentOutOfRangeException ||
                                      e is ArgumentNullException || e is FormatException || e is InvalidOperationException || e is OverflowException)
            {
                return null;
            }
        }

        public static string FearGreedLabel(int value)
        {
            if (value <= 24) return Loc.T("Extreme fear");
            if (value <= 44) return Loc.T("Fear");
            if (value <= 55) return Loc.T("Neutral");
            if (value <= 75) return Loc.T("Greed");
            return Loc.T("Extreme greed");
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            try
            {
                using (var response = await _http.GetAsync(url, ct))
                {
                    if ((int)response.StatusCode == 429)
                        throw new MarketException(Loc.T("The free price API asked to wait a bit. It will retry on its own"));
                    if (!response.IsSuccessStatusCode)
                        throw new MarketException(Loc.F("The price service returned an error ({0})", (int)response.StatusCode));
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (HttpRequestException)
            {
                throw new MarketException(Loc.T("No connection to the price service"));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new MarketException(Loc.T("The price service took too long"));
            }
        }

        private static decimal? Dec(JsonValue e, string name)
        {
            JsonValue v;
            if (!e.TryGet(name, out v) || !v.IsNumber) return null;
            decimal d;
            if (v.TryDecimal(out d)) return d;
            var x = v.AsDouble();
            if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 7.9e28) return null;
            return (decimal)x;
        }

        private static string Str(JsonValue e, string name)
        {
            JsonValue v;
            return e.TryGet(name, out v) && v.IsString ? v.AsString() : null;
        }

        private sealed class CachedChart
        {
            public CachedChart(DateTime at, IReadOnlyList<ChartPoint> points)
            {
                At = at;
                Points = points;
            }

            public DateTime At { get; }
            public IReadOnlyList<ChartPoint> Points { get; }
        }

        private sealed class CachedMarkets
        {
            public CachedMarkets(DateTime at, string currency, IReadOnlyList<MarketCoin> coins)
            {
                At = at;
                Currency = currency;
                Coins = coins;
            }

            public DateTime At { get; }
            public string Currency { get; }
            public IReadOnlyList<MarketCoin> Coins { get; }
        }
    }
}
