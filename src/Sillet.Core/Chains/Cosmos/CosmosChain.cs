using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Cosmos
{
    public static class Proto
    {
        public static byte[] Varint(ulong value)
        {
            var list = new List<byte>();
            while (value >= 0x80)
            {
                list.Add((byte)(value | 0x80));
                value >>= 7;
            }
            list.Add((byte)value);
            return list.ToArray();
        }

        public static byte[] Bytes(int field, byte[] data)
        {
            return Encoding.Bytes.Concat(Varint((ulong)((field << 3) | 2)), Varint((ulong)data.Length), data);
        }

        public static byte[] String(int field, string text)
        {
            return Bytes(field, System.Text.Encoding.UTF8.GetBytes(text));
        }

        public static byte[] UInt(int field, ulong value)
        {
            return Encoding.Bytes.Concat(Varint((ulong)(field << 3)), Varint(value));
        }
    }

    public sealed class CosmosChain : ChainBase
    {
        private readonly ApiPool _api = new ApiPool("cosmos", "https://cosmos-rest.publicnode.com", "https://rest.cosmos.directory/cosmoshub", "https://lcd-cosmoshub.keplr.app");

        public override string Id { get { return "cosmos"; } }
        public override string Name { get { return "Cosmos Hub"; } }
        public override Asset Native { get; } = new Asset("ATOM", "Cosmos", 6);
        public override string DerivationPath { get { return "m/44'/118'/" + Account + "'/0/0"; } }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveSecp256k1(seed, DerivationPath);
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return AddressFromPublicKey(Secp256k1.PublicKey(privateKey, true));
        }

        public static string AddressFromPublicKey(byte[] compressedPublicKey)
        {
            return Bech32.EncodeBytes("cosmos", Hashes.Hash160(compressedPublicKey));
        }

        public static bool TrySplit(string text, out string address, out string memo)
        {
            address = null;
            memo = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int cut = text.IndexOfAny(new[] { ':', '?', ' ' });
            address = cut > 0 ? text.Substring(0, cut) : text;
            if (cut > 0)
            {
                memo = text.Substring(cut + 1).Trim();
                if (memo.StartsWith("memo=", StringComparison.OrdinalIgnoreCase)) memo = memo.Substring(5);
                if (memo.Length > 256) return false;
            }
            var bytes = Bech32.DecodeBytes("cosmos", address);
            return bytes != null && bytes.Length == 20;
        }

        public override bool IsValidAddress(string address)
        {
            string plain, memo;
            return TrySplit(address, out plain, out memo);
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://www.mintscan.io/cosmos/address/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://www.mintscan.io/cosmos/tx/" + txId;
        }

        private async Task<BigInteger> BalanceAsync(string address, CancellationToken ct)
        {
            var json = await _api.GetAsync("/cosmos/bank/v1beta1/balances/" + address, ct);
            foreach (var coin in json["balances"].Items)
                if (coin["denom"].AsString() == "uatom") return BigInteger.Parse(coin["amount"].AsString());
            return BigInteger.Zero;
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            return Balance(asset, await BalanceAsync(Address, ct));
        }

        public static byte[] BuildTxBody(string from, string to, ulong amount, string memo)
        {
            var coin = Encoding.Bytes.Concat(Proto.String(1, "uatom"), Proto.String(2, amount.ToString()));
            var msg = Encoding.Bytes.Concat(Proto.String(1, from), Proto.String(2, to), Proto.Bytes(3, coin));
            var any = Encoding.Bytes.Concat(Proto.String(1, "/cosmos.bank.v1beta1.MsgSend"), Proto.Bytes(2, msg));
            var body = Proto.Bytes(1, any);
            if (!string.IsNullOrEmpty(memo)) body = Encoding.Bytes.Concat(body, Proto.String(2, memo));
            return body;
        }

        public static byte[] BuildAuthInfo(byte[] publicKey, ulong sequence, ulong feeAmount, ulong gasLimit)
        {
            var key = Proto.Bytes(1, publicKey);
            var keyAny = Encoding.Bytes.Concat(Proto.String(1, "/cosmos.crypto.secp256k1.PubKey"), Proto.Bytes(2, key));
            var single = Proto.UInt(1, 1);
            var mode = Proto.Bytes(1, single);
            var signer = Encoding.Bytes.Concat(Proto.Bytes(1, keyAny), Proto.Bytes(2, mode), sequence == 0 ? new byte[0] : Proto.UInt(3, sequence));
            var coin = Encoding.Bytes.Concat(Proto.String(1, "uatom"), Proto.String(2, feeAmount.ToString()));
            var fee = Encoding.Bytes.Concat(Proto.Bytes(1, coin), Proto.UInt(2, gasLimit));
            return Encoding.Bytes.Concat(Proto.Bytes(1, signer), Proto.Bytes(2, fee));
        }

        public static byte[] BuildSignDoc(byte[] body, byte[] authInfo, string chainId, ulong accountNumber)
        {
            return Encoding.Bytes.Concat(Proto.Bytes(1, body), Proto.Bytes(2, authInfo), Proto.String(3, chainId), accountNumber == 0 ? new byte[0] : Proto.UInt(4, accountNumber));
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            string destination, memo;
            if (!TrySplit(to, out destination, out memo)) throw new WalletException("The recipient address is not valid for Cosmos Hub");
            var raw = CheckTransfer(asset, destination, amount, sendMax);
            ulong accountNumber = 0, sequence = 0;
            try
            {
                var account = await _api.GetAsync("/cosmos/auth/v1beta1/accounts/" + Address, ct).ConfigureAwait(false);
                var info = account["account"];
                if (info.Has("base_account")) info = info["base_account"];
                accountNumber = info.Has("account_number") ? info["account_number"].AsULong() : 0;
                sequence = info.Has("sequence") ? info["sequence"].AsULong() : 0;
            }
            catch (WalletException e) when (e.Message.Contains("(404)") || e.Message.Contains("(400)") || e.Message.Contains("(500)"))
            {
                throw new WalletException("Your Cosmos account has no ATOM yet");
            }
            var node = await _api.GetAsync("/cosmos/base/tendermint/v1beta1/node_info", ct);
            var chainId = node["default_node_info"]["network"].AsString();
            if (chainId != "cosmoshub-4") throw new WalletException("This node is not on the Cosmos Hub. Try again later");

            var balance = await BalanceAsync(Address, ct);
            const ulong gasLimit = 100000;
            const ulong fee = 2500;
            BigInteger send = sendMax ? balance - fee : raw;
            if (send <= 0) throw new WalletException("You do not have enough ATOM to send");
            if (send + fee > balance) throw Insufficient(Native, send + fee, balance);

            var body = BuildTxBody(Address, destination, (ulong)send, memo);
            var publicKey = Secp256k1.PublicKey(Key, true);
            var authInfo = BuildAuthInfo(publicKey, sequence, fee, gasLimit);
            var doc = BuildSignDoc(body, authInfo, chainId, accountNumber);
            var signature = Secp256k1.Sign(Hashes.Sha256(doc), Key);
            var rawSignature = Bytes.Concat(signature.R, signature.S);
            var txRaw = Bytes.Concat(Proto.Bytes(1, body), Proto.Bytes(2, authInfo), Proto.Bytes(3, rawSignature));

            return new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = destination,
                Amount = Units.FromRaw(send, 6),
                Fee = Units.FromRaw(fee, 6),
                FeeAsset = Native,
                TxId = Hex.Encode(Hashes.Sha256(txRaw)).ToUpperInvariant(),
                Payload = Convert.ToBase64String(txRaw),
            };
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var result = await _api.PostAsync("/cosmos/tx/v1beta1/txs", new { tx_bytes = (string)transfer.Payload, mode = "BROADCAST_MODE_SYNC" }, ct);
            var response = result["tx_response"];
            if (response["code"].AsInt() != 0) throw new WalletException("The network rejected the transaction: " + response["raw_log"].AsString());
            return response["txhash"].AsString();
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var result = await _api.GetAsync("/cosmos/tx/v1beta1/txs/" + txId, ct);
                return result["tx_response"]["code"].AsInt() == 0 ? TxStage.Final : TxStage.Failed;
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
                return TxStage.Pending;
            }
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, 6);
                var query = "?query=transfer.recipient%3D%27" + Address + "%27&order_by=ORDER_BY_DESC&pagination.limit=10";
                var result = await _api.GetAsync("/cosmos/tx/v1beta1/txs" + query, ct);
                foreach (var tx in result["tx_responses"].Items)
                {
                    if (tx["code"].AsInt() != 0) continue;
                    foreach (var message in tx["tx"]["body"]["messages"].Items)
                    {
                        if (message["@type"].AsString() != "/cosmos.bank.v1beta1.MsgSend" || message["to_address"].AsString() != Address) continue;
                        foreach (var coin in message["amount"].Items)
                            if (coin["denom"].AsString() == "uatom" && BigInteger.Parse(coin["amount"].AsString()) == raw)
                                return new IncomingTransfer(tx["txhash"].AsString(), message["from_address"].AsString(), amount);
                    }
                }
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
            }
            return null;
        }
    }
}
