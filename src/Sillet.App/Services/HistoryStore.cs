// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Sillet.Net;
using Sillet.Storage;

namespace Sillet.App.Services
{
    public sealed class HistoryEntry
    {
        public HistoryEntry(DateTime timeUtc, string chainId, string network, string symbol, decimal amount, decimal fee, string feeSymbol,
            string from, string to, string txId, string explorerUrl, bool incoming = false)
        {
            TimeUtc = timeUtc;
            ChainId = chainId;
            Network = network;
            Symbol = symbol;
            Amount = amount;
            Fee = fee;
            FeeSymbol = feeSymbol;
            From = from;
            To = to;
            TxId = txId;
            ExplorerUrl = explorerUrl;
            Incoming = incoming;
        }

        public DateTime TimeUtc { get; }
        public string ChainId { get; }
        public string Network { get; }
        public string Symbol { get; }
        public decimal Amount { get; }
        public decimal Fee { get; }
        public string FeeSymbol { get; }
        public string From { get; }
        public string To { get; }
        public string TxId { get; }
        public string ExplorerUrl { get; }
        public bool Incoming { get; }
        public string OwnAddress => Incoming ? To : From;
    }

    public sealed class HistoryStore
    {
        private const int MaxEntries = 500;
        private readonly string _path;
        private readonly List<HistoryEntry> _entries = new List<HistoryEntry>();

        public HistoryStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "history.json");
            Load();
        }

        public IReadOnlyList<HistoryEntry> Entries
        {
            get
            {
                lock (this) return _entries.ToList();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Add(HistoryEntry entry)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            Save();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RemoveFrom(ICollection<string> addresses)
        {
            _entries.RemoveAll(e => addresses.Contains(e.OwnAddress));
            Save();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public int RemoveWhere(Predicate<HistoryEntry> match)
        {
            var removed = _entries.RemoveAll(match);
            if (removed > 0) Save();
            return removed;
        }

        private void Load()
        {
            _entries.Clear();
            try
            {
                if (!File.Exists(_path)) return;
                var root = JsonValue.Parse(LocalData.ReadText(_path));
                if (!root.IsArray) return;
                foreach (var e in root.Items)
                {
                    try
                    {
                        _entries.Add(new HistoryEntry(
                            DateTime.Parse(e["time"].AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                            e["chain"].AsString(), e["network"].AsString(), e["symbol"].AsString(),
                            decimal.Parse(e["amount"].AsString(), NumberStyles.Float, CultureInfo.InvariantCulture),
                            decimal.Parse(e["fee"].AsString(), NumberStyles.Float, CultureInfo.InvariantCulture),
                            e["feeSymbol"].AsString(), e["from"].AsString(), e["to"].AsString(), e["txid"].AsString(), e["explorer"].AsString(),
                            e.Has("in") && e["in"].AsString() == "1"));
                    }
                    catch (Exception ex) when (ex is FormatException || ex is KeyNotFoundException || ex is InvalidOperationException || ex is OverflowException)
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is InvalidOperationException)
            {
            }
        }

        private void Save()
        {
            try
            {
                var list = _entries.Select(e => new Dictionary<string, object>
                {
                    { "time", e.TimeUtc.ToString("o", CultureInfo.InvariantCulture) },
                    { "chain", e.ChainId },
                    { "network", e.Network },
                    { "symbol", e.Symbol },
                    { "amount", e.Amount.ToString(CultureInfo.InvariantCulture) },
                    { "fee", e.Fee.ToString(CultureInfo.InvariantCulture) },
                    { "feeSymbol", e.FeeSymbol },
                    { "from", e.From },
                    { "to", e.To },
                    { "txid", e.TxId },
                    { "explorer", e.ExplorerUrl },
                    { "in", e.Incoming ? "1" : "0" },
                }).ToList();
                LocalData.WriteText(_path, Json.Serialize(list));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
