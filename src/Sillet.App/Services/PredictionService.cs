// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public sealed class PredictionOption
    {
        public PredictionOption(string label, int percent)
        {
            Label = label;
            Percent = percent;
        }

        public string Label { get; }
        public int Percent { get; }
    }

    public sealed class PredictionEvent
    {
        public PredictionEvent(string title, string url, decimal volume, DateTime? ends, IReadOnlyList<PredictionOption> options, int more)
        {
            Title = title;
            Url = url;
            Volume = volume;
            Ends = ends;
            Options = options;
            More = more;
        }

        public string Title { get; }
        public string Url { get; }
        public decimal Volume { get; }
        public DateTime? Ends { get; }
        public IReadOnlyList<PredictionOption> Options { get; }
        public int More { get; }
    }

    public sealed class PredictionService
    {
        private const string Api = "https://gamma-api.polymarket.com/events?active=true&closed=false&limit=15";
        public static readonly string[] Categories = { "trending", "new", "sports", "crypto", "politics" };
        private readonly HttpClient _http;
        private readonly Dictionary<string, KeyValuePair<DateTime, IReadOnlyList<PredictionEvent>>> _cache = new Dictionary<string, KeyValuePair<DateTime, IReadOnlyList<PredictionEvent>>>();

        private static string UrlFor(string category)
        {
            switch (category)
            {
                case "new": return Api + "&order=startDate&ascending=false";
                case "sports": case "crypto": case "politics": return Api + "&tag_slug=" + category + "&order=volume24hr&ascending=false";
                default: return Api + "&order=volume24hr&ascending=false";
            }
        }

        public PredictionService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/1.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<IReadOnlyList<PredictionEvent>> GetAsync(string category, bool force, CancellationToken ct = default(CancellationToken))
        {
            if (!AppSettings.PredictionsAllowed) return new List<PredictionEvent>();
            KeyValuePair<DateTime, IReadOnlyList<PredictionEvent>> hit;
            if (!force && _cache.TryGetValue(category, out hit) && DateTime.UtcNow - hit.Key < TimeSpan.FromMinutes(5)) return hit.Value;
            string json;
            using (var response = await _http.GetAsync(UrlFor(category), ct))
            {
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync();
            }
            var list = Parse(json);
            _cache[category] = new KeyValuePair<DateTime, IReadOnlyList<PredictionEvent>>(DateTime.UtcNow, list);
            return list;
        }

        public static IReadOnlyList<PredictionEvent> Parse(string json)
        {
            var root = JsonValue.Parse(json);
            if (!root.IsArray) throw new FormatException("Unexpected predictions format");
            var result = new List<PredictionEvent>();
            foreach (var e in root.Items)
            {
                var title = Text(e, "title");
                var slug = Text(e, "slug");
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(slug)) continue;
                JsonValue markets;
                if (!e.TryGet("markets", out markets) || !markets.IsArray) continue;
                var options = new List<PredictionOption>();
                foreach (var m in markets.Items)
                {
                    JsonValue closed;
                    if (m.TryGet("closed", out closed) && closed.IsTrue) continue;
                    var yes = YesPrice(m);
                    if (!yes.HasValue) continue;
                    var label = Text(m, "groupItemTitle");
                    if (string.IsNullOrEmpty(label)) label = Text(m, "question");
                    options.Add(new PredictionOption(label ?? "", (int)Math.Round(yes.Value * 100m)));
                }
                if (options.Count == 0) continue;
                List<PredictionOption> shown;
                int more = 0;
                if (options.Count == 1)
                {
                    shown = new List<PredictionOption> { new PredictionOption("Chance", options[0].Percent) };
                }
                else
                {
                    shown = options.OrderByDescending(o => o.Percent).Take(3).ToList();
                    more = options.Count - shown.Count;
                }
                DateTime ends;
                DateTime? endDate = null;
                var endText = Text(e, "endDate");
                if (endText != null && DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out ends)) endDate = ends;
                decimal volume = 0;
                JsonValue v;
                if (e.TryGet("volume", out v) && v.IsNumber) volume = v.AsDecimal();
                result.Add(new PredictionEvent(title, "https://polymarket.com/event/" + slug, volume, endDate, shown, more));
            }
            return result;
        }

        private static string Text(JsonValue obj, string name)
        {
            JsonValue v;
            return obj.TryGet(name, out v) && v.IsString ? v.AsString() : null;
        }

        private static decimal? YesPrice(JsonValue market)
        {
            var raw = Text(market, "outcomePrices");
            if (raw == null) return null;
            try
            {
                var prices = JsonValue.Parse(raw);
                if (!prices.IsArray || prices.Count == 0) return null;
                var first = prices[0];
                decimal value;
                if (first.IsNumber) value = first.AsDecimal();
                else if (!(first.IsString && decimal.TryParse(first.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))) return null;
                return Math.Max(0m, Math.Min(1m, value));
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException)
            {
                return null;
            }
        }
    }
}
