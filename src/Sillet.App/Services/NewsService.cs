using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public sealed class NewsItem
    {
        public NewsItem(string id, string author, string description, string title, string date, string url)
        {
            Id = id;
            Author = author;
            Description = description;
            Title = title;
            RawDate = date;
            Url = url;
        }

        public string AvatarName
        {
            get
            {
                var author = (Author ?? "").Trim().TrimStart('@');
                return string.Equals(author, "moizefala", StringComparison.OrdinalIgnoreCase) ? "moizefala" : "silly";
            }
        }
        public string Id { get; }
        public string Author { get; }
        public string Description { get; }
        public string Title { get; }
        public string RawDate { get; }
        public string Date => NewsDates.Describe(RawDate, DateTime.Now);
        public string Url { get; }
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public bool HasUrl => !string.IsNullOrEmpty(Url);
        public bool HasDate => !string.IsNullOrEmpty(RawDate);
        public string AuthorInitial => string.IsNullOrEmpty(Author) ? "?" : Author.Substring(0, 1).ToUpperInvariant();
    }

    public static class NewsDates
    {
        public static string Describe(string raw, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            raw = raw.Trim();
            DateTime when;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out when)) return raw;
            var culture = Loc.Instance.Culture;
            var days = (now.Date - when.Date).Days;
            if (days < 0 || days > 7) return when.ToString(when.Year == now.Year ? "d MMM" : "d MMM yyyy", culture);
            var time = raw.IndexOf(':') >= 0 ? when.ToString("t", culture) : null;
            if (days == 0) return time == null ? Loc.T("Today") : Loc.F("Today at {0}", time);
            if (days == 1) return time == null ? Loc.T("Yesterday") : Loc.F("Yesterday at {0}", time);
            return Loc.F("{0} days ago", days);
        }
    }

    public sealed class NewsService
    {
        public const string FeedUrl = "https://raton.fun/wallet/news.json";

        private readonly HttpClient _http;

        public NewsService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/1.0");
        }

        private static string CacheFile
        {
            get { return Path.Combine(AppSettings.DataFolder, "cache", "news.json"); }
        }

        private static IReadOnlyList<NewsItem> _memory;
        private static DateTime _memoryUtc = DateTime.MinValue;
        private static readonly TimeSpan MemoryLife = TimeSpan.FromSeconds(45);

        public IReadOnlyList<NewsItem> GetCached()
        {
            var known = _memory;
            if (known != null) return known;
            try
            {
                if (File.Exists(CacheFile)) return TryParse(File.ReadAllText(CacheFile)) ?? new NewsItem[0];
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return new NewsItem[0];
        }

        public async Task<IReadOnlyList<NewsItem>> GetAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!AppSettings.NewsAllowed) return new List<NewsItem>();
            var recent = _memory;
            if (recent != null && DateTime.UtcNow - _memoryUtc < MemoryLife) return recent;
            var fresh = await DownloadAsync(ct);
            if (fresh.Count > 0)
            {
                _memory = fresh;
                _memoryUtc = DateTime.UtcNow;
            }
            return fresh;
        }

        private async Task<IReadOnlyList<NewsItem>> DownloadAsync(CancellationToken ct)
        {
            string json = null;
            try
            {
                using (var response = await _http.GetAsync(FeedUrl + "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute, ct))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        json = await response.Content.ReadAsStringAsync();
                        var parsed = TryParse(json);
                        if (parsed != null)
                        {
                            TryWriteCache(json);
                            return parsed;
                        }
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
            {
            }

            try
            {
                if (File.Exists(CacheFile)) return TryParse(File.ReadAllText(CacheFile)) ?? new NewsItem[0];
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return new NewsItem[0];
        }

        public static IReadOnlyList<NewsItem> TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return Parse(json);
            }
            catch (Exception e) when (IsParseError(e))
            {
            }
            try
            {
                return Parse(Repair(json));
            }
            catch (Exception e) when (IsParseError(e))
            {
                return null;
            }
        }

        public static string Repair(string json)
        {
            var text = json.Trim().TrimStart((char)0xFEFF);
            text = text.Replace((char)0x201C, '"').Replace((char)0x201D, '"');
            text = Regex.Replace(text, @"(""|\d|true|false|null|\}|\])(\s*\r?\n\s*)(""|\{)", "$1,$2$3");
            text = Regex.Replace(text, @",(\s*[\}\]])", "$1");
            return text;
        }

        private static bool IsParseError(Exception e)
        {
            return e is FormatException || e is InvalidOperationException || e is KeyNotFoundException || e is OverflowException;
        }

        public static IReadOnlyList<NewsItem> Parse(string json)
        {
            var root = JsonValue.Parse(json);
            JsonValue list;
            if (root.IsArray) list = root;
            else if (root.IsObject && root.TryGet("news", out list) && list.IsArray) { }
            else throw new FormatException("Expected a list of news");

            var items = new List<NewsItem>();
            foreach (var n in list.Items)
            {
                if (!n.IsObject) continue;
                var description = Text(n, "description");
                if (string.IsNullOrWhiteSpace(description)) continue;
                var url = Text(n, "url");
                if (url != null && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = null;
                items.Add(new NewsItem(Text(n, "id") ?? (items.Count + 1).ToString(CultureInfo.InvariantCulture),
                    Text(n, "author") ?? "Sillet", WithoutFinalPeriod(description), WithoutFinalPeriod(Text(n, "title")), Text(n, "date"), url));
            }

            return items
                .Select((item, index) => new { item, index })
                .OrderByDescending(x => NumericId(x.item.Id))
                .ThenBy(x => x.index)
                .Select(x => x.item)
                .ToList();
        }

        public static string WithoutFinalPeriod(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Trim();
            while (text.EndsWith(".") && !text.EndsWith("...")) text = text.Substring(0, text.Length - 1).TrimEnd();
            return text;
        }

        private static double NumericId(string id)
        {
            double value;
            return double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : double.MinValue;
        }

        private static string Text(JsonValue item, string name)
        {
            JsonValue v;
            if (!item.TryGet(name, out v) || v.IsNull) return null;
            if (v.IsString) return v.AsString();
            if (v.IsNumber) return v.ToJson();
            return null;
        }

        private static void TryWriteCache(string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                File.WriteAllText(CacheFile, json);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}