// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Net;

namespace Sillet.Chains.Utxo
{
    public sealed class AddressInfo
    {
        public AddressInfo(long balance, long txCount)
        {
            Balance = balance;
            TxCount = txCount;
        }

        public long Balance { get; }
        public long TxCount { get; }
    }

    public sealed class ProviderTx
    {
        public ProviderTx(string txId, long received, string from)
        {
            TxId = txId;
            Received = received;
            From = from ?? "";
        }

        public string TxId { get; }
        public long Received { get; }
        public string From { get; }
    }

    public sealed class TxDetail
    {
        public bool Confirmed;
        public readonly List<TxDetailInput> Inputs = new List<TxDetailInput>();
        public readonly List<TxDetailOutput> Outputs = new List<TxDetailOutput>();
    }

    public sealed class TxDetailInput
    {
        public string TxId;
        public uint Vout;
        public long Value;
        public string Address;
    }

    public sealed class TxDetailOutput
    {
        public long Value;
        public string Address;
        public byte[] Script;
    }

    public interface IUtxoProvider
    {
        Task<IList<ProviderTx>> GetRecentTxsAsync(string address, CancellationToken ct);
        Task<AddressInfo> GetAddressInfoAsync(string address, CancellationToken ct);
        Task<IList<Utxo>> GetUtxosAsync(string address, CancellationToken ct);
        Task<long> GetBalanceAsync(string address, CancellationToken ct);
        Task<decimal> GetFeeRateAsync(FeeSpeed speed, CancellationToken ct);
        Task<string> BroadcastAsync(string rawHex, CancellationToken ct);
        Task<int> GetConfirmationsAsync(string txId, CancellationToken ct);
        Task<TxDetail> GetTxDetailAsync(string txId, CancellationToken ct);
    }

    public sealed class EsploraProvider : IUtxoProvider
    {
        private readonly string[] _defaultUrls;
        private readonly string _chainId;

        public EsploraProvider(string chainId, params string[] baseUrls)
        {
            _chainId = chainId;
            _defaultUrls = baseUrls;
        }

        private string[] _baseUrls
        {
            get { return Endpoints.For(_chainId, _defaultUrls); }
        }

        public async Task<IList<Utxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            var json = await GetAsync("/address/" + address + "/utxo", ct);
            return json.Items.Select(u => new Utxo(
                u["txid"].AsString(),
                u["vout"].AsUInt(),
                u["value"].AsLong(),
                u["status"]["confirmed"].AsBool())).ToList();
        }

        public async Task<long> GetBalanceAsync(string address, CancellationToken ct)
        {
            var json = await GetAsync("/address/" + address, ct);
            return NetBalance(json["chain_stats"]) + NetBalance(json["mempool_stats"]);
        }

        public async Task<IList<ProviderTx>> GetRecentTxsAsync(string address, CancellationToken ct)
        {
            var json = await GetAsync("/address/" + address + "/txs", ct);
            var list = new List<ProviderTx>();
            foreach (var tx in json.Items.Take(15))
            {
                long received = 0;
                foreach (var o in tx["vout"].Items)
                {
                    JsonValue a;
                    if (o.TryGet("scriptpubkey_address", out a) && a.IsString && a.AsString() == address) received += o["value"].AsLong();
                }
                if (received == 0) continue;
                string from = "";
                foreach (var i in tx["vin"].Items)
                {
                    JsonValue prevout, a;
                    if (i.TryGet("prevout", out prevout) && prevout.IsObject && prevout.TryGet("scriptpubkey_address", out a) && a.IsString)
                    {
                        if (a.AsString() == address) { from = ""; break; }
                        if (from.Length == 0) from = a.AsString();
                    }
                }
                list.Add(new ProviderTx(tx["txid"].AsString(), received, from));
            }
            return list;
        }

        public async Task<AddressInfo> GetAddressInfoAsync(string address, CancellationToken ct)
        {
            var json = await GetAsync("/address/" + address, ct);
            var stats = json["chain_stats"];
            var pool = json["mempool_stats"];
            return new AddressInfo(NetBalance(stats) + NetBalance(pool), stats["tx_count"].AsLong() + pool["tx_count"].AsLong());
        }

        private static long NetBalance(JsonValue stats)
        {
            return stats["funded_txo_sum"].AsLong() - stats["spent_txo_sum"].AsLong();
        }

        public async Task<decimal> GetFeeRateAsync(FeeSpeed speed, CancellationToken ct)
        {
            try
            {
                var json = await GetAsync("/v1/fees/recommended", ct);
                var key = speed == FeeSpeed.Fast ? "fastestFee" : speed == FeeSpeed.Slow ? "hourFee" : "halfHourFee";
                return json[key].AsDecimal();
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException)
            {
                var json = await GetAsync("/fee-estimates", ct);
                var target = speed == FeeSpeed.Fast ? "1" : speed == FeeSpeed.Slow ? "6" : "3";
                return json[target].AsDecimal();
            }
        }

        public async Task<string> BroadcastAsync(string rawHex, CancellationToken ct)
        {
            WalletException last = null;
            foreach (var url in _baseUrls)
            {
                try
                {
                    return await Http.PostTextAsync(url + "/tx", rawHex, ct);
                }
                catch (HttpRequestException e)
                {
                    last = new WalletException("Could not connect to broadcast", e);
                }
            }
            throw last ?? new WalletException("Could not broadcast");
        }

        public async Task<TxDetail> GetTxDetailAsync(string txId, CancellationToken ct)
        {
            JsonValue json;
            try
            {
                json = await GetAsync("/tx/" + txId, ct);
            }
            catch (WalletException)
            {
                return null;
            }
            var detail = new TxDetail { Confirmed = json["status"]["confirmed"].AsBool() };
            foreach (var vin in json["vin"].Items)
            {
                var prevout = vin["prevout"];
                JsonValue address;
                detail.Inputs.Add(new TxDetailInput
                {
                    TxId = vin["txid"].AsString(),
                    Vout = vin["vout"].AsUInt(),
                    Value = prevout["value"].AsLong(),
                    Address = prevout.TryGet("scriptpubkey_address", out address) && !address.IsNull ? address.AsString() : "",
                });
            }
            foreach (var vout in json["vout"].Items)
            {
                JsonValue address;
                detail.Outputs.Add(new TxDetailOutput
                {
                    Value = vout["value"].AsLong(),
                    Address = vout.TryGet("scriptpubkey_address", out address) && !address.IsNull ? address.AsString() : "",
                    Script = Sillet.Encoding.Hex.Decode(vout["scriptpubkey"].AsString()),
                });
            }
            return detail;
        }

        public async Task<int> GetConfirmationsAsync(string txId, CancellationToken ct)
        {
            JsonValue status;
            try
            {
                status = await GetAsync("/tx/" + txId + "/status", ct);
            }
            catch (WalletException)
            {
                return -1;
            }
            if (!status.IsObject || !status["confirmed"].AsBool()) return 0;
            var tip = (await GetAsync("/blocks/tip/height", ct)).AsLong();
            return (int)Math.Max(1, tip - status["block_height"].AsLong() + 1);
        }

        private async Task<JsonValue> GetAsync(string path, CancellationToken ct)
        {
            Exception last = null;
            foreach (var url in _baseUrls)
            {
                try
                {
                    return await Http.GetJsonAsync(url + path, ct);
                }
                catch (Exception e) when ((e is HttpRequestException || e is TaskCanceledException || e is WalletException) && !ct.IsCancellationRequested)
                {
                    last = e;
                }
            }
            throw new WalletException("Could not connect to the network. Check your internet connection", last);
        }
    }

    public sealed class BlockCypherProvider : IUtxoProvider
    {
        private readonly string _coin;

        public BlockCypherProvider(string coin)
        {
            _coin = coin;
        }

        private string Base
        {
            get { return "https://api.blockcypher.com/v1/" + _coin + "/main"; }
        }

        public async Task<IList<Utxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            var json = await Http.GetJsonAsync(Base + "/addrs/" + address + "?unspentOnly=true&includeScript=false&limit=2000", ct);
            JsonValue refs;
            if (!json.TryGet("txrefs", out refs)) return new List<Utxo>();
            return refs.Items
                .Where(r => r["tx_output_n"].AsInt() >= 0)
                .Select(r => new Utxo(r["tx_hash"].AsString(), (uint)r["tx_output_n"].AsInt(), r["value"].AsLong()))
                .ToList();
        }

        public async Task<long> GetBalanceAsync(string address, CancellationToken ct)
        {
            var json = await Http.GetJsonAsync(Base + "/addrs/" + address + "/balance", ct);
            return json["final_balance"].AsLong();
        }

        public async Task<IList<ProviderTx>> GetRecentTxsAsync(string address, CancellationToken ct)
        {
            var json = await Http.GetJsonAsync(Base + "/addrs/" + address + "?limit=15&includeScript=false", ct);
            var sums = new Dictionary<string, long>();
            var order = new List<string>();
            foreach (var key in new[] { "unconfirmed_txrefs", "txrefs" })
            {
                JsonValue refs;
                if (!json.TryGet(key, out refs) || !refs.IsArray) continue;
                foreach (var r in refs.Items)
                {
                    if (r["tx_output_n"].AsInt() < 0) continue;
                    var hash = r["tx_hash"].AsString();
                    long sum;
                    if (!sums.TryGetValue(hash, out sum)) order.Add(hash);
                    sums[hash] = sum + r["value"].AsLong();
                }
            }
            return order.Select(h => new ProviderTx(h, sums[h], "")).ToList();
        }

        public async Task<AddressInfo> GetAddressInfoAsync(string address, CancellationToken ct)
        {
            var json = await Http.GetJsonAsync(Base + "/addrs/" + address + "/balance", ct);
            return new AddressInfo(json["final_balance"].AsLong(), json["final_n_tx"].AsLong());
        }

        public async Task<decimal> GetFeeRateAsync(FeeSpeed speed, CancellationToken ct)
        {
            var json = await Http.GetJsonAsync(Base, ct);
            var key = speed == FeeSpeed.Fast ? "high_fee_per_kb" : speed == FeeSpeed.Slow ? "low_fee_per_kb" : "medium_fee_per_kb";
            return json[key].AsDecimal() / 1000m;
        }

        public Task<TxDetail> GetTxDetailAsync(string txId, CancellationToken ct)
        {
            throw new WalletException("This network does not support speeding up a transaction");
        }

        public async Task<int> GetConfirmationsAsync(string txId, CancellationToken ct)
        {
            try
            {
                var json = await Http.GetJsonAsync(Base + "/txs/" + txId, ct);
                JsonValue confirmations;
                return json.TryGet("confirmations", out confirmations) && !confirmations.IsNull ? confirmations.AsInt() : 0;
            }
            catch (System.Net.Http.HttpRequestException)
            {
                return -1;
            }
        }

        public async Task<string> BroadcastAsync(string rawHex, CancellationToken ct)
        {
            var json = await Http.PostJsonAsync(Base + "/txs/push", new { tx = rawHex }, ct);
            JsonValue error;
            if (json.TryGet("error", out error)) throw new WalletException("The network rejected the transaction: " + error);
            return json["tx"]["hash"].AsString();
        }
    }
}

namespace Sillet.Chains.Utxo
{
    public sealed class BlockbookProvider : IUtxoProvider
    {
        private readonly ApiPool _api;
        private readonly Func<string, string> _normalize;

        public BlockbookProvider(string chainId, Func<string, string> normalize, params string[] baseUrls)
        {
            _api = new ApiPool(chainId, baseUrls);
            _normalize = normalize ?? (a => a);
        }

        public async Task<IList<Utxo>> GetUtxosAsync(string address, CancellationToken ct)
        {
            var json = await _api.GetAsync("/utxo/" + address, ct);
            return json.Items.Select(u => new Utxo(
                u["txid"].AsString(),
                u["vout"].AsUInt(),
                long.Parse(u["value"].AsString()),
                u.Has("confirmations") && u["confirmations"].AsInt() > 0)).ToList();
        }

        public async Task<long> GetBalanceAsync(string address, CancellationToken ct)
        {
            return (await GetAddressInfoAsync(address, ct)).Balance;
        }

        public async Task<AddressInfo> GetAddressInfoAsync(string address, CancellationToken ct)
        {
            var json = await _api.GetAsync("/address/" + address + "?details=basic", ct);
            long balance = long.Parse(json["balance"].AsString());
            long pending = json.Has("unconfirmedBalance") ? long.Parse(json["unconfirmedBalance"].AsString()) : 0;
            long count = json["txs"].AsLong() + (json.Has("unconfirmedTxs") ? json["unconfirmedTxs"].AsLong() : 0);
            return new AddressInfo(balance + pending, count);
        }

        public async Task<IList<ProviderTx>> GetRecentTxsAsync(string address, CancellationToken ct)
        {
            var json = await _api.GetAsync("/address/" + address + "?details=txs&pageSize=15", ct);
            var list = new List<ProviderTx>();
            JsonValue txs;
            if (!json.TryGet("transactions", out txs) || !txs.IsArray) return list;
            var mine = _normalize(address);
            foreach (var tx in txs.Items)
            {
                long received = 0;
                foreach (var o in tx["vout"].Items)
                {
                    JsonValue addresses;
                    if (o.TryGet("addresses", out addresses) && addresses.IsArray && addresses.Items.Any(a => _normalize(a.AsString()) == mine))
                        received += long.Parse(o["value"].AsString());
                }
                if (received == 0) continue;
                string from = "";
                foreach (var i in tx["vin"].Items)
                {
                    JsonValue addresses;
                    if (i.TryGet("addresses", out addresses) && addresses.IsArray && addresses.Count > 0)
                    {
                        var a = addresses[0].AsString();
                        if (_normalize(a) == mine) { from = ""; break; }
                        if (from.Length == 0) from = a;
                    }
                }
                list.Add(new ProviderTx(tx["txid"].AsString(), received, from));
            }
            return list;
        }

        public Task<decimal> GetFeeRateAsync(FeeSpeed speed, CancellationToken ct)
        {
            return Task.FromResult(speed == FeeSpeed.Fast ? 2m : 1.1m);
        }

        public async Task<string> BroadcastAsync(string rawHex, CancellationToken ct)
        {
            var json = await _api.GetAsync("/sendtx/" + rawHex, ct);
            JsonValue error;
            if (json.TryGet("error", out error)) throw new WalletException("The network rejected the transaction: " + (error.IsObject ? error["message"].AsString() : error.AsString()));
            return json["result"].AsString();
        }

        public Task<TxDetail> GetTxDetailAsync(string txId, CancellationToken ct)
        {
            throw new WalletException("This network does not support speeding up a transaction");
        }

        public async Task<int> GetConfirmationsAsync(string txId, CancellationToken ct)
        {
            try
            {
                var json = await _api.GetAsync("/tx/" + txId, ct);
                return json.Has("confirmations") ? json["confirmations"].AsInt() : 0;
            }
            catch (WalletException)
            {
                return -1;
            }
        }
    }
}
