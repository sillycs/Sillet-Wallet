using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Sillet.Net;

namespace Sillet.App.Services
{
    public sealed class AppNotification
    {
        public AppNotification(string id, string kind, string title, string message, DateTime timeUtc, bool isRead, string target)
        {
            Id = id;
            Kind = kind;
            Title = title;
            Message = message;
            TimeUtc = timeUtc;
            IsRead = isRead;
            Target = target;
        }

        public string Id { get; }
        public string Kind { get; }
        public string Title { get; }
        public string Message { get; }
        public DateTime TimeUtc { get; }
        public bool IsRead { get; set; }
        public string Target { get; }
    }

    public sealed class BalanceReading
    {
        public BalanceReading(string key, string symbol, string network, decimal balance)
        {
            Key = key;
            Symbol = symbol;
            Network = network;
            Balance = balance;
        }

        public string Key { get; }
        public string Symbol { get; }
        public string Network { get; }
        public decimal Balance { get; }
        public string ChainId => Key.Substring(0, Math.Max(0, Key.IndexOf(':')));
        public string Address
        {
            get
            {
                var first = Key.IndexOf(':');
                var last = Key.LastIndexOf(':');
                return first < 0 || last <= first ? "" : Key.Substring(first + 1, last - first - 1);
            }
        }
    }

    public sealed class NotificationStore
    {
        public const int MaxItems = 60;
        private readonly string _path;
        private readonly List<AppNotification> _items = new List<AppNotification>();
        private readonly Dictionary<string, decimal> _balances = new Dictionary<string, decimal>();
        private long _lastNewsId = -1;
        private string _btcCurrency;
        private decimal _btcReference;

        public NotificationStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "notifications.json");
            Load();
        }

        public IReadOnlyList<AppNotification> Items
        {
            get
            {
                lock (this) return _items.ToList();
            }
        }

        public int UnreadCount
        {
            get
            {
                lock (this) return _items.Count(n => !n.IsRead);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public AppNotification Add(string kind, string title, string message, string target, DateTime nowUtc)
        {
            var item = new AppNotification(Guid.NewGuid().ToString("N"), kind, title, message, nowUtc, false, target);
            _items.Insert(0, item);
            if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            Save();
            return item;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Remove(string id)
        {
            if (_items.RemoveAll(n => n.Id == id) > 0) Save();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public int RemoveWhere(Predicate<AppNotification> match)
        {
            var removed = _items.RemoveAll(match);
            if (removed > 0) Save();
            return removed;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void MarkAllRead()
        {
            if (_items.All(n => n.IsRead)) return;
            foreach (var n in _items) n.IsRead = true;
            Save();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Clear()
        {
            _items.Clear();
            Save();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public List<BalanceReading> DetectPayments(IEnumerable<BalanceReading> readings)
        {
            var received = new List<BalanceReading>();
            bool changed = false;
            foreach (var r in readings)
            {
                decimal previous;
                if (_balances.TryGetValue(r.Key, out previous))
                {
                    var diff = r.Balance - previous;
                    if (diff > 0 && diff > previous * 0.0000001m)
                        received.Add(new BalanceReading(r.Key, r.Symbol, r.Network, diff));
                }
                if (!_balances.TryGetValue(r.Key, out previous) || previous != r.Balance)
                {
                    _balances[r.Key] = r.Balance;
                    changed = true;
                }
            }
            if (changed) Save();
            return received;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public List<NewsItem> DetectNews(IEnumerable<NewsItem> news)
        {
            var list = news.Select(n => new { Item = n, Number = ParseId(n.Id) }).Where(n => n.Number >= 0).ToList();
            if (list.Count == 0) return new List<NewsItem>();
            var max = list.Max(n => n.Number);
            if (_lastNewsId < 0)
            {
                _lastNewsId = max;
                Save();
                return new List<NewsItem>();
            }
            var fresh = list.Where(n => n.Number > _lastNewsId).OrderBy(n => n.Number).Select(n => n.Item).ToList();
            if (max > _lastNewsId)
            {
                _lastNewsId = max;
                Save();
            }
            return fresh;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public decimal? DetectBitcoinMove(decimal price, string currency, decimal percent)
        {
            if (price <= 0 || percent <= 0) return null;
            if (_btcCurrency != currency || _btcReference <= 0)
            {
                _btcCurrency = currency;
                _btcReference = price;
                Save();
                return null;
            }
            var change = (price - _btcReference) / _btcReference * 100m;
            if (Math.Abs(change) < percent) return null;
            _btcReference = price;
            Save();
            return change;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ResetBitcoin()
        {
            _btcCurrency = null;
            _btcReference = 0;
            Save();
        }

        private static long ParseId(string id)
        {
            long n;
            return long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }

        private void Load()
        {
            var json = JsonFileStore.Read(_path);
            if (json == null) return;
            JsonValue value;
            if (json.TryGet("Items", out value) && value.Kind == JsonKind.Array)
            {
                foreach (var item in value.Items)
                {
                    var id = JsonFileStore.Text(item, "Id");
                    var title = JsonFileStore.Text(item, "Title");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) continue;
                    DateTime time;
                    if (!DateTime.TryParse(JsonFileStore.Text(item, "TimeUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) time = DateTime.UtcNow;
                    JsonValue read;
                    _items.Add(new AppNotification(id, JsonFileStore.Text(item, "Kind") ?? "info", title, JsonFileStore.Text(item, "Message") ?? "", time,
                        item.TryGet("IsRead", out read) && read.IsTrue, JsonFileStore.Text(item, "Target")));
                }
            }
            if (json.TryGet("Balances", out value) && value.Kind == JsonKind.Array)
            {
                foreach (var item in value.Items)
                {
                    var key = JsonFileStore.Text(item, "Key");
                    var amount = JsonFileStore.Number(item, "Amount");
                    if (key != null && amount.HasValue) _balances[key] = amount.Value;
                }
            }
            var news = JsonFileStore.Number(json, "LastNewsId");
            if (news.HasValue) _lastNewsId = (long)news.Value;
            _btcCurrency = JsonFileStore.Text(json, "BtcCurrency");
            _btcReference = JsonFileStore.Number(json, "BtcReference") ?? 0;
        }

        private void Save()
        {
            JsonFileStore.Write(_path, new
            {
                LastNewsId = _lastNewsId,
                BtcCurrency = _btcCurrency ?? "",
                BtcReference = _btcReference,
                Balances = _balances.Select(b => new { Key = b.Key, Amount = b.Value }).ToList(),
                Items = _items.Select(n => new
                {
                    Id = n.Id,
                    Kind = n.Kind,
                    Title = n.Title,
                    Message = n.Message,
                    TimeUtc = n.TimeUtc.ToString("o", CultureInfo.InvariantCulture),
                    IsRead = n.IsRead,
                    Target = n.Target ?? "",
                }).ToList(),
            });
        }
    }
}
