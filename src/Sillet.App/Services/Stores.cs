// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Sillet.App.Services
{
    public sealed class Contact
    {
        public Contact(string id, string name, string chainId, string network, string address)
        {
            Id = id;
            Name = name;
            ChainId = chainId;
            Network = network;
            Address = address;
        }

        public string Id { get; }
        public string Name { get; }
        public string ChainId { get; }
        public string Network { get; }
        public string Address { get; }
        public string ShortAddress => Formatters.ShortAddress(Address);
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    }

    public sealed class ContactStore
    {
        private readonly string _path;
        private readonly List<Contact> _items = new List<Contact>();

        public ContactStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "contacts.json");
            var root = JsonFileStore.Read(_path);
            if (root == null || !root.IsArray) return;
            foreach (var c in root.Items)
            {
                if (!c.IsObject) continue;
                var address = JsonFileStore.Text(c, "address");
                var chain = JsonFileStore.Text(c, "chain");
                if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(chain)) continue;
                _items.Add(new Contact(JsonFileStore.Text(c, "id") ?? Guid.NewGuid().ToString("N"), JsonFileStore.Text(c, "name") ?? "Contact",
                    chain, JsonFileStore.Text(c, "network") ?? chain, address));
            }
        }

        public IReadOnlyList<Contact> Items => _items;

        public Contact Add(string name, string chainId, string network, string address)
        {
            var existing = _items.FirstOrDefault(c => c.ChainId == chainId && c.Address == address);
            if (existing != null) _items.Remove(existing);
            var contact = new Contact(Guid.NewGuid().ToString("N"), name.Trim(), chainId, network, address);
            _items.Add(contact);
            Save();
            return contact;
        }

        public void Remove(Contact contact)
        {
            _items.RemoveAll(c => c.Id == contact.Id);
            Save();
        }

        public Contact Find(string chainId, string address)
        {
            return _items.FirstOrDefault(c => c.ChainId == chainId && c.Address == address);
        }

        private void Save()
        {
            JsonFileStore.Write(_path, _items.Select(c => new Dictionary<string, object>
            {
                { "id", c.Id }, { "name", c.Name }, { "chain", c.ChainId }, { "network", c.Network }, { "address", c.Address },
            }).ToList());
        }
    }

    public sealed class AccountStore
    {
        private readonly string _path;
        private readonly SortedDictionary<int, string> _names = new SortedDictionary<int, string>();

        public AccountStore(string path)
        {
            _path = path;
            var root = JsonFileStore.Read(_path);
            if (root != null && root.IsObject)
            {
                var active = JsonFileStore.Text(root, "active");
                int parsed;
                if (int.TryParse(active, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed >= 0 && parsed < Sillet.WalletManager.MaxAccounts) Active = parsed;
                Sillet.Net.JsonValue list;
                if (root.TryGet("accounts", out list) && list.IsArray)
                    foreach (var a in list.Items)
                    {
                        int index;
                        if (!a.IsObject || !int.TryParse(JsonFileStore.Text(a, "index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;
                        if (index < 0 || index >= Sillet.WalletManager.MaxAccounts) continue;
                        _names[index] = JsonFileStore.Text(a, "name") ?? "";
                    }
            }
            if (!_names.ContainsKey(0)) _names[0] = "";
            if (!_names.ContainsKey(Active)) _names[Active] = "";
        }

        public static string PathFor(string profileId)
        {
            return Path.Combine(AppSettings.DataFolder, profileId == null || profileId == Sillet.Storage.ProfileStore.MainId ? "accounts.json" : "accounts-" + profileId + ".json");
        }

        public int Active { get; private set; }
        public IReadOnlyList<int> Indexes => _names.Keys.ToList();
        public bool CanAdd => _names.Count < Sillet.WalletManager.MaxAccounts;

        public string Name(int index)
        {
            string name;
            if (_names.TryGetValue(index, out name) && !string.IsNullOrWhiteSpace(name)) return name;
            return index == 0 ? Loc.T("Main account") : Loc.F("Account {0}", index + 1);
        }

        public int Add(string name)
        {
            for (int i = 0; i < Sillet.WalletManager.MaxAccounts; i++)
                if (!_names.ContainsKey(i))
                {
                    _names[i] = (name ?? "").Trim();
                    Save();
                    return i;
                }
            return -1;
        }

        public void Rename(int index, string name)
        {
            if (!_names.ContainsKey(index)) return;
            _names[index] = (name ?? "").Trim();
            Save();
        }

        public void SetActive(int index)
        {
            if (!_names.ContainsKey(index)) return;
            Active = index;
            Save();
        }

        private void Save()
        {
            JsonFileStore.Write(_path, new Dictionary<string, object>
            {
                { "active", Active.ToString(CultureInfo.InvariantCulture) },
                { "accounts", _names.Select(n => new Dictionary<string, object> { { "index", n.Key.ToString(CultureInfo.InvariantCulture) }, { "name", n.Value } }).ToList() },
            });
        }
    }

    public sealed class CustomToken
    {
        public CustomToken(string chainId, string contract, string symbol, string name, int decimals)
        {
            ChainId = chainId;
            Contract = contract;
            Symbol = symbol;
            Name = name;
            Decimals = decimals;
        }

        public string ChainId { get; }
        public string Contract { get; }
        public string Symbol { get; }
        public string Name { get; }
        public int Decimals { get; }
    }

    public sealed class TokenStore
    {
        private readonly string _path;
        private readonly List<CustomToken> _items = new List<CustomToken>();

        public TokenStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "tokens.json");
            var root = JsonFileStore.Read(_path);
            if (root == null || !root.IsArray) return;
            foreach (var t in root.Items)
            {
                if (!t.IsObject) continue;
                int decimals;
                var chain = JsonFileStore.Text(t, "chain");
                var contract = JsonFileStore.Text(t, "contract");
                if (string.IsNullOrEmpty(chain) || string.IsNullOrEmpty(contract)) continue;
                if (!int.TryParse(JsonFileStore.Text(t, "decimals"), NumberStyles.Integer, CultureInfo.InvariantCulture, out decimals) || decimals < 0 || decimals > 36) continue;
                _items.Add(new CustomToken(chain, contract, JsonFileStore.Text(t, "symbol") ?? "TOKEN", JsonFileStore.Text(t, "name") ?? "Token", decimals));
            }
        }

        public IReadOnlyList<CustomToken> Items => _items;

        public void Add(CustomToken token)
        {
            _items.RemoveAll(t => t.ChainId == token.ChainId && string.Equals(t.Contract, token.Contract, StringComparison.OrdinalIgnoreCase));
            _items.Add(token);
            Save();
        }

        public void Remove(string chainId, string contract)
        {
            _items.RemoveAll(t => t.ChainId == chainId && string.Equals(t.Contract, contract, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public static string SafeSymbol(string symbol, IEnumerable<string> reserved)
        {
            var taken = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
            foreach (var k in MarketService.CoinIds.Keys) taken.Add(k);
            return taken.Contains(symbol) ? symbol + " (custom)" : symbol;
        }

        private void Save()
        {
            JsonFileStore.Write(_path, _items.Select(t => new Dictionary<string, object>
            {
                { "chain", t.ChainId }, { "contract", t.Contract }, { "symbol", t.Symbol }, { "name", t.Name }, { "decimals", t.Decimals.ToString(CultureInfo.InvariantCulture) },
            }).ToList());
        }
    }

    public sealed class PriceAlert
    {
        public PriceAlert(string id, string coinId, string symbol, bool above, decimal price, string currency)
        {
            Id = id;
            CoinId = coinId;
            Symbol = symbol;
            Above = above;
            Price = price;
            Currency = currency;
        }

        public string Id { get; }
        public string CoinId { get; }
        public string Symbol { get; }
        public bool Above { get; }
        public decimal Price { get; }
        public string Currency { get; }
        public string Text => Loc.F(Above ? "Above {0}" : "Below {0}", Formatters.Fiat(Price, Currency));
        public string Message => Loc.F(Above ? "{0} is above {1}" : "{0} is below {1}", Symbol, Formatters.Fiat(Price, Currency));
    }

    public sealed class AlertStore
    {
        private readonly string _path;
        private readonly List<PriceAlert> _items = new List<PriceAlert>();

        public AlertStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "alerts.json");
            var root = JsonFileStore.Read(_path);
            if (root == null || !root.IsArray) return;
            foreach (var a in root.Items)
            {
                if (!a.IsObject) continue;
                var price = JsonFileStore.Number(a, "price");
                var coin = JsonFileStore.Text(a, "coin");
                if (!price.HasValue || coin == null) continue;
                _items.Add(new PriceAlert(JsonFileStore.Text(a, "id") ?? Guid.NewGuid().ToString("N"), coin, JsonFileStore.Text(a, "symbol") ?? coin,
                    JsonFileStore.Text(a, "direction") != "below", price.Value, JsonFileStore.Text(a, "currency") ?? "usd"));
            }
        }

        public IReadOnlyList<PriceAlert> Items => _items;

        public PriceAlert Add(string coinId, string symbol, bool above, decimal price, string currency)
        {
            var alert = new PriceAlert(Guid.NewGuid().ToString("N"), coinId, symbol, above, price, currency.ToLowerInvariant());
            _items.Add(alert);
            Save();
            return alert;
        }

        public void Remove(PriceAlert alert)
        {
            _items.RemoveAll(a => a.Id == alert.Id);
            Save();
        }

        public List<PriceAlert> Check(IEnumerable<MarketCoin> coins, string currency)
        {
            var prices = coins.ToDictionary(c => c.Id, c => c.Price);
            var hit = _items.Where(a => string.Equals(a.Currency, currency, StringComparison.OrdinalIgnoreCase) && prices.ContainsKey(a.CoinId) &&
                                        prices[a.CoinId] > 0 && (a.Above ? prices[a.CoinId] >= a.Price : prices[a.CoinId] <= a.Price)).ToList();
            if (hit.Count == 0) return hit;
            _items.RemoveAll(a => hit.Contains(a));
            Save();
            return hit;
        }

        private void Save()
        {
            JsonFileStore.Write(_path, _items.Select(a => new Dictionary<string, object>
            {
                { "id", a.Id }, { "coin", a.CoinId }, { "symbol", a.Symbol }, { "direction", a.Above ? "above" : "below" },
                { "price", a.Price.ToString(CultureInfo.InvariantCulture) }, { "currency", a.Currency },
            }).ToList());
        }
    }

    public sealed class SnapshotStore
    {
        private const int MaxDays = 400;
        private readonly string _path;
        private readonly Dictionary<string, SortedDictionary<DateTime, decimal>> _series = new Dictionary<string, SortedDictionary<DateTime, decimal>>();

        public SnapshotStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "portfolio.json");
            var root = JsonFileStore.Read(_path);
            if (root == null || !root.IsObject) return;
            foreach (var currency in new[] { "clp", "usd" })
            {
                Sillet.Net.JsonValue list;
                if (!root.TryGet(currency, out list) || !list.IsArray) continue;
                var series = Series(currency);
                foreach (var p in list.Items)
                {
                    DateTime day;
                    var date = JsonFileStore.Text(p, "date");
                    var value = JsonFileStore.Number(p, "value");
                    if (date != null && value.HasValue && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                        series[day] = value.Value;
                }
            }
        }

        public void Record(string currency, decimal total, DateTime today)
        {
            var series = Series(currency.ToLowerInvariant());
            series[today.Date] = total;
            while (series.Count > MaxDays) series.Remove(series.Keys.First());
            Save();
        }

        public IReadOnlyList<ChartPoint> Points(string currency)
        {
            return Series(currency.ToLowerInvariant()).Select(p => new ChartPoint(p.Key, (double)p.Value)).ToList();
        }

        private SortedDictionary<DateTime, decimal> Series(string currency)
        {
            SortedDictionary<DateTime, decimal> s;
            if (!_series.TryGetValue(currency, out s))
            {
                s = new SortedDictionary<DateTime, decimal>();
                _series[currency] = s;
            }
            return s;
        }

        private void Save()
        {
            var data = new Dictionary<string, object>();
            foreach (var pair in _series)
                data[pair.Key] = pair.Value.Select(p => new Dictionary<string, object>
                {
                    { "date", p.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                    { "value", p.Value.ToString(CultureInfo.InvariantCulture) },
                }).ToList();
            JsonFileStore.Write(_path, data);
        }
    }

    public sealed class SwapRecord
    {
        public SwapRecord(string id, DateTime timeUtc, string fromSymbol, string fromChain, string fromNetwork, string toSymbol, string toChain, string toNetwork,
            decimal amount, decimal amountTo, string depositAddress, string status, string txIn)
        {
            Id = id;
            TimeUtc = timeUtc;
            FromSymbol = fromSymbol;
            FromChain = fromChain;
            FromNetwork = fromNetwork;
            ToSymbol = toSymbol;
            ToChain = toChain;
            ToNetwork = toNetwork;
            Amount = amount;
            AmountTo = amountTo;
            DepositAddress = depositAddress;
            Status = status;
            TxIn = txIn;
        }

        public string Id { get; }
        public DateTime TimeUtc { get; }
        public string FromSymbol { get; }
        public string FromChain { get; }
        public string FromNetwork { get; }
        public string ToSymbol { get; }
        public string ToChain { get; }
        public string ToNetwork { get; }
        public decimal Amount { get; }
        public decimal AmountTo { get; }
        public string DepositAddress { get; }
        public string Status { get; set; }
        public string TxIn { get; }
    }

    public sealed class SwapStore
    {
        private readonly string _path;
        private readonly List<SwapRecord> _items = new List<SwapRecord>();

        public SwapStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "swaps.json");
            var root = JsonFileStore.Read(_path);
            if (root == null || !root.IsArray) return;
            foreach (var r in root.Items)
            {
                if (!r.IsObject) continue;
                var id = JsonFileStore.Text(r, "id");
                DateTime time;
                if (id == null || !DateTime.TryParse(JsonFileStore.Text(r, "time") ?? "", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out time)) continue;
                _items.Add(new SwapRecord(id, time, JsonFileStore.Text(r, "fromSymbol"), JsonFileStore.Text(r, "fromChain"), JsonFileStore.Text(r, "fromNetwork"),
                    JsonFileStore.Text(r, "toSymbol"), JsonFileStore.Text(r, "toChain"), JsonFileStore.Text(r, "toNetwork"),
                    JsonFileStore.Number(r, "amount") ?? 0, JsonFileStore.Number(r, "amountTo") ?? 0, JsonFileStore.Text(r, "deposit"),
                    JsonFileStore.Text(r, "status") ?? "wait", JsonFileStore.Text(r, "txIn")));
            }
        }

        public IReadOnlyList<SwapRecord> Items => _items;

        public void Add(SwapRecord record)
        {
            _items.Insert(0, record);
            Save();
        }

        public void Update(string id, string status)
        {
            var r = _items.FirstOrDefault(x => x.Id == id);
            if (r == null || r.Status == status) return;
            r.Status = status;
            Save();
        }

        public SwapRecord ByDeposit(string address)
        {
            return _items.FirstOrDefault(x => x.DepositAddress == address);
        }

        private void Save()
        {
            JsonFileStore.Write(_path, _items.Select(r => new Dictionary<string, object>
            {
                { "id", r.Id }, { "time", r.TimeUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "fromSymbol", r.FromSymbol }, { "fromChain", r.FromChain }, { "fromNetwork", r.FromNetwork },
                { "toSymbol", r.ToSymbol }, { "toChain", r.ToChain }, { "toNetwork", r.ToNetwork },
                { "amount", r.Amount.ToString(CultureInfo.InvariantCulture) }, { "amountTo", r.AmountTo.ToString(CultureInfo.InvariantCulture) },
                { "deposit", r.DepositAddress }, { "status", r.Status }, { "txIn", r.TxIn },
            }).ToList());
        }
    }
}
