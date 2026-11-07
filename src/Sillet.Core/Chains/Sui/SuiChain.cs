// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Sui
{
    public sealed class SuiCoin
    {
        public byte[] Id;
        public ulong Version;
        public byte[] Digest;
        public BigInteger Balance;
    }

    public static class SuiTransaction
    {
        private static byte[] Uleb(int value)
        {
            var list = new List<byte>();
            uint v = (uint)value;
            while (v >= 0x80)
            {
                list.Add((byte)(v | 0x80));
                v >>= 7;
            }
            list.Add((byte)v);
            return list.ToArray();
        }

        private static byte[] PureBytes(byte[] data)
        {
            return Bytes.Concat(new byte[] { 0x00 }, Uleb(data.Length), data);
        }

        public static byte[] Build(byte[] sender, byte[] recipient, ulong amount, bool sendAll, IList<SuiCoin> gas, ulong price, ulong budget)
        {
            var parts = new List<byte[]>();
            parts.Add(new byte[] { 0x00, 0x00 });
            if (sendAll)
            {
                parts.Add(Uleb(1));
                parts.Add(PureBytes(recipient));
                parts.Add(Uleb(1));
                parts.Add(new byte[] { 0x01 });
                parts.Add(Uleb(1));
                parts.Add(new byte[] { 0x00 });
                parts.Add(new byte[] { 0x01, 0x00, 0x00 });
            }
            else
            {
                parts.Add(Uleb(2));
                parts.Add(PureBytes(Bytes.UInt64LE(amount)));
                parts.Add(PureBytes(recipient));
                parts.Add(Uleb(2));
                parts.Add(new byte[] { 0x02, 0x00 });
                parts.Add(Uleb(1));
                parts.Add(new byte[] { 0x01, 0x00, 0x00 });
                parts.Add(new byte[] { 0x01 });
                parts.Add(Uleb(1));
                parts.Add(new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00 });
                parts.Add(new byte[] { 0x01, 0x01, 0x00 });
            }
            parts.Add(sender);
            parts.Add(Uleb(gas.Count));
            foreach (var coin in gas)
            {
                parts.Add(coin.Id);
                parts.Add(Bytes.UInt64LE(coin.Version));
                parts.Add(Uleb(coin.Digest.Length));
                parts.Add(coin.Digest);
            }
            parts.Add(sender);
            parts.Add(Bytes.UInt64LE(price));
            parts.Add(Bytes.UInt64LE(budget));
            parts.Add(new byte[] { 0x00 });
            return Bytes.Concat(parts.ToArray());
        }

        public static byte[] Digest(byte[] tx)
        {
            return Hashes.Blake2b(Bytes.Concat(System.Text.Encoding.ASCII.GetBytes("TransactionData::"), tx), 256);
        }

        public static string Sign(byte[] seed, byte[] tx)
        {
            var message = Hashes.Blake2b(Bytes.Concat(new byte[] { 0, 0, 0 }, tx), 256);
            var signature = Ed25519Keys.Sign(seed, message);
            return Convert.ToBase64String(Bytes.Concat(new byte[] { 0x00 }, signature, Ed25519Keys.PublicKey(seed)));
        }
    }

    public sealed class SuiChain : ChainBase
    {
        private const string CoinType = "0x2::sui::SUI";
        private readonly JsonRpcClient _rpc = new JsonRpcClient("sui", new[] { "https://fullnode.mainnet.sui.io:443", "https://sui-rpc.publicnode.com" });

        public override string Id { get { return "sui"; } }
        public override string Name { get { return "Sui"; } }
        public override Asset Native { get; } = new Asset("SUI", "Sui", 9);
        public override string DerivationPath { get { return "m/44'/784'/" + Account + "'/0'/0'"; } }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveEd25519(seed, DerivationPath);
        }

        public static string AddressFromPublicKey(byte[] publicKey)
        {
            return "0x" + Hex.Encode(Hashes.Blake2b(Bytes.Concat(new byte[] { 0x00 }, publicKey), 256));
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return AddressFromPublicKey(Ed25519Keys.PublicKey(privateKey));
        }

        private static bool TryParse(string address, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(address) || address.Length != 66 || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
            try { bytes = Hex.Decode(address.Substring(2)); }
            catch (FormatException) { return false; }
            return bytes.Length == 32;
        }

        public override bool IsValidAddress(string address)
        {
            byte[] bytes;
            return TryParse((address ?? "").Trim(), out bytes);
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://suivision.xyz/account/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://suivision.xyz/txblock/" + txId;
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            var result = await _rpc.CallAsync("suix_getBalance", new object[] { Address, CoinType }, ct);
            return Balance(asset, BigInteger.Parse(result["totalBalance"].AsString()));
        }

        private async Task<List<SuiCoin>> CoinsAsync(CancellationToken ct)
        {
            var coins = new List<SuiCoin>();
            string cursor = null;
            for (int page = 0; page < 5; page++)
            {
                var result = await _rpc.CallAsync("suix_getCoins", new object[] { Address, CoinType, cursor, 50 }, ct);
                foreach (var item in result["data"].Items)
                    coins.Add(new SuiCoin
                    {
                        Id = Hex.Decode(item["coinObjectId"].AsString().Substring(2)),
                        Version = item["version"].AsULong(),
                        Digest = Base58.Bitcoin.Decode(item["digest"].AsString()),
                        Balance = BigInteger.Parse(item["balance"].AsString()),
                    });
                if (!result["hasNextPage"].IsTrue) break;
                cursor = result["nextCursor"].AsString();
            }
            return coins.OrderByDescending(c => c.Balance).ToList();
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            to = (to ?? "").Trim().ToLowerInvariant();
            var raw = CheckTransfer(asset, to, amount, sendMax);
            var coins = await CoinsAsync(ct);
            if (coins.Count == 0) throw new WalletException("You do not have any SUI");
            BigInteger total = 0;
            foreach (var coin in coins) total += coin.Balance;
            var price = (await _rpc.CallAsync("suix_getReferenceGasPrice", new object[0], ct)).AsULong();
            var gas = coins.Take(250).ToList();

            var sender = Hex.Decode(Address.Substring(2));
            var recipient = Hex.Decode(to.Substring(2));
            ulong budget = 50000000;
            ulong estimate = 4000000;
            try
            {
                var probe = SuiTransaction.Build(sender, recipient, sendMax ? 0UL : (ulong)raw, sendMax, gas, price, budget);
                var dry = await _rpc.CallAsync("sui_dryRunTransactionBlock", new object[] { Convert.ToBase64String(probe) }, ct);
                var used = dry["effects"]["gasUsed"];
                long net = used["computationCost"].AsLong() + used["storageCost"].AsLong() - used["storageRebate"].AsLong();
                long cost = Math.Max(used["computationCost"].AsLong(), net);
                if (cost > 0) estimate = (ulong)cost;
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
            }
            budget = Math.Min(50000000UL, Math.Max(estimate + estimate / 3, 3000000UL));
            if (estimate > budget) estimate = budget;

            BigInteger send = sendMax ? total - budget : raw;
            if (send <= 0) throw new WalletException("You do not have enough SUI to cover the network fee");
            if (send + budget > total && !sendMax)
                throw Insufficient(Native, send + estimate, total);

            var tx = SuiTransaction.Build(sender, recipient, (ulong)send, sendMax, gas, price, budget);
            var digest = Base58.Bitcoin.Encode(SuiTransaction.Digest(tx));
            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = to,
                Amount = Units.FromRaw(sendMax ? total - estimate : send, 9),
                Fee = Units.FromRaw(estimate, 9),
                FeeAsset = Native,
                TxId = digest,
                Payload = new[] { Convert.ToBase64String(tx), SuiTransaction.Sign(Key, tx) },
            };
            return transfer;
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var payload = (string[])transfer.Payload;
            try
            {
                var options = new { showEffects = true };
                var result = await _rpc.CallAsync("sui_executeTransactionBlock", new object[] { payload[0], new[] { payload[1] }, options, "WaitForEffectsCert" }, ct);
                var status = result["effects"]["status"];
                if (status["status"].AsString() != "success") throw new WalletException("The network rejected the transaction: " + (status.Has("error") ? status["error"].AsString() : "failed"));
                return result["digest"].AsString();
            }
            catch (RpcException e)
            {
                throw new WalletException("The network rejected the transaction: " + e.Message);
            }
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var result = await _rpc.CallAsync("sui_getTransactionBlock", new object[] { txId, new { showEffects = true } }, ct);
                return result["effects"]["status"]["status"].AsString() == "success" ? TxStage.Final : TxStage.Failed;
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
                return TxStage.Pending;
            }
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, 9);
                var query = new { filter = new { ToAddress = Address }, options = new { showBalanceChanges = true, showInput = true } };
                var result = await _rpc.CallAsync("suix_queryTransactionBlocks", new object[] { query, null, 10, true }, ct);
                foreach (var tx in result["data"].Items)
                {
                    if (!tx.Has("balanceChanges")) continue;
                    foreach (var change in tx["balanceChanges"].Items)
                    {
                        var owner = change["owner"];
                        if (!owner.IsObject || !owner.Has("AddressOwner") || owner["AddressOwner"].AsString() != Address) continue;
                        if (change["coinType"].AsString() != CoinType || BigInteger.Parse(change["amount"].AsString()) != raw) continue;
                        string from = "";
                        JsonValue data;
                        if (tx.TryGet("transaction", out data) && data.IsObject) from = data["data"]["sender"].AsString();
                        return new IncomingTransfer(tx["digest"].AsString(), from, amount);
                    }
                }
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
            }
            return null;
        }
    }
}
