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

    public interface IUtxoProvider
    {
        Task<IList<ProviderTx>> GetRecentTxsAsync(string address, CancellationToken ct);
        Task<AddressInfo> GetAddressInfoAsync(string address, CancellationToken ct);
        Task<IList<Utxo>> GetUtxosAsync(string address, CancellationToken ct);
        Task<long> GetBalanceAsync(string address, CancellationToken ct);
        Task<decimal> GetFeeRateAsync(FeeSpeed speed, CancellationToken ct);
        Task<string> BroadcastAsync(string rawHex, CancellationToken ct);
        /// <summary>-1 when the network does not know the transaction, 0 while it waits, otherwise how many blocks confirm it.</summary>
        Task<int> GetConfirmationsAsync(string txId, CancellationToken ct);
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
