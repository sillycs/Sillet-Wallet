using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Xrp
{
    public sealed class XrpChain : ChainBase
    {
        public static readonly Base58 Ripple = new Base58("rpshnaf39wBUDNEGHJKLM4PQRST7VWXYZ2bcdeCg65jkm8oFqi1tuvAxyz");

        private readonly ApiPool _api = new ApiPool("xrp", "https://xrplcluster.com", "https://s1.ripple.com:51234", "https://s2.ripple.com:51234");

        public override string Id { get { return "xrp"; } }
        public override string Name { get { return "XRP Ledger"; } }
        public override Asset Native { get; } = new Asset("XRP", "XRP", 6);
        public override string DerivationPath { get { return "m/44'/144'/" + Account + "'/0/0"; } }

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
            return Ripple.EncodeCheck(Bytes.Concat(new byte[] { 0x00 }, Hashes.Hash160(compressedPublicKey)));
        }

        public static bool TryDecodeAddress(string address, out byte[] accountId)
        {
            accountId = null;
            if (string.IsNullOrEmpty(address) || address[0] != 'r') return false;
            var payload = Ripple.DecodeCheck(address);
            if (payload == null || payload.Length != 21 || payload[0] != 0x00) return false;
            accountId = Bytes.Skip(payload, 1);
            return true;
        }

        public static bool TrySplitDestination(string text, out string address, out uint? tag)
        {
            address = null;
            tag = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            int cut = text.IndexOfAny(new[] { ':', '?', '#', ' ' });
            string rest = null;
            if (cut > 0)
            {
                address = text.Substring(0, cut);
                rest = text.Substring(cut + 1).Trim();
            }
            else address = text;
            byte[] id;
            if (!TryDecodeAddress(address, out id)) return false;
            if (!string.IsNullOrEmpty(rest))
            {
                if (rest.StartsWith("dt=", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(3);
                uint value;
                if (!uint.TryParse(rest, out value)) return false;
                tag = value;
            }
            return true;
        }

        public override bool IsValidAddress(string address)
        {
            string plain;
            uint? tag;
            return TrySplitDestination(address, out plain, out tag);
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://xrpscan.com/account/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://xrpscan.com/tx/" + txId;
        }

        private async Task<JsonValue> CallAsync(string method, object parameters, CancellationToken ct)
        {
            var response = await _api.PostAsync("", new { method = method, @params = new[] { parameters } }, ct).ConfigureAwait(false);
            var result = response["result"];
            return result;
        }

        private static bool NotFound(JsonValue result)
        {
            JsonValue error;
            return result.TryGet("error", out error) && error.IsString && error.AsString() == "actNotFound";
        }

        private async Task<JsonValue> AccountAsync(string address, string ledger, CancellationToken ct)
        {
            var result = await CallAsync("account_info", new { account = address, ledger_index = ledger, strict = true }, ct);
            if (NotFound(result)) return null;
            JsonValue error;
            if (result.TryGet("error", out error) && error.IsString) throw new WalletException("XRP Ledger: " + error.AsString());
            return result;
        }

        private async Task<long[]> ReserveAsync(CancellationToken ct)
        {
            var result = await CallAsync("server_info", new { }, ct);
            var ledger = result["info"]["validated_ledger"];
            long baseDrops = (long)Math.Round(ledger["reserve_base_xrp"].AsDecimal() * 1000000m);
            long incDrops = (long)Math.Round(ledger["reserve_inc_xrp"].AsDecimal() * 1000000m);
            return new[] { baseDrops, incDrops };
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            var info = await AccountAsync(Address, "validated", ct);
            return Balance(asset, info == null ? BigInteger.Zero : BigInteger.Parse(info["account_data"]["Balance"].AsString()));
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            string destination;
            uint? tag;
            if (!TrySplitDestination(to, out destination, out tag)) throw new WalletException("The recipient address is not valid for XRP Ledger");
            var raw = CheckTransfer(asset, destination, amount, sendMax);
            var mine = await AccountAsync(Address, "current", ct);
            if (mine == null) throw new WalletException("Your XRP account is not active yet. Send at least 1 XRP to your address first");
            var data = mine["account_data"];
            var balance = BigInteger.Parse(data["Balance"].AsString());
            long owners = data.Has("OwnerCount") ? data["OwnerCount"].AsLong() : 0;
            var reserve = await ReserveAsync(ct);
            long locked = reserve[0] + reserve[1] * owners;

            var feeResult = await CallAsync("fee", new { }, ct);
            long fee = 12;
            JsonValue drops;
            if (feeResult.TryGet("drops", out drops) && drops.IsObject)
            {
                long open = drops["open_ledger_fee"].AsLong();
                long median = drops["median_fee"].AsLong();
                fee = Math.Max(Math.Max(open, median), 10);
                fee = fee + fee / 5 + 1;
            }
            if (fee > 1000000) throw new WalletException("The network fee is unusually high right now. Try again later");

            BigInteger sendDrops;
            if (sendMax)
            {
                sendDrops = balance - locked - fee;
                if (sendDrops <= 0) throw new WalletException("You do not have enough XRP to send after the account reserve");
            }
            else
            {
                sendDrops = raw;
                if (sendDrops + fee > balance) throw Insufficient(Native, sendDrops + fee, balance);
                if (balance - sendDrops - fee < locked)
                    throw new WalletException("XRP Ledger keeps " + Units.FromRaw(locked, 6) + " XRP as reserve in your account. You can send up to " +
                                              Units.FromRaw(BigInteger.Max(0, balance - locked - fee), 6) + " XRP");
            }

            byte[] destinationId;
            TryDecodeAddress(destination, out destinationId);
            var destInfo = await AccountAsync(destination, "current", ct);
            if (destInfo == null && sendDrops < reserve[0])
                throw new WalletException("That account is new: you must send at least " + Units.FromRaw(reserve[0], 6) + " XRP to activate it");
            if (destInfo != null && destInfo["account_data"].Has("Flags") && (destInfo["account_data"]["Flags"].AsLong() & 0x00020000) != 0 && tag == null)
                throw new WalletException("This address requires a destination tag. Use the format address:tag");

            uint sequence = data["Sequence"].AsUInt();
            uint ledgerIndex = (await CallAsync("ledger_current", new { }, ct))["ledger_current_index"].AsUInt();
            var unsigned = Serialize((ulong)sendDrops, (ulong)fee, sequence, ledgerIndex + 20, tag, Secp256k1.PublicKey(Key, true), AccountId(Address), destinationId, null);
            var hash = HashWithPrefix(new byte[] { 0x53, 0x54, 0x58, 0x00 }, unsigned);
            var signature = Secp256k1.Sign(hash, Key).ToDer();
            var blob = Serialize((ulong)sendDrops, (ulong)fee, sequence, ledgerIndex + 20, tag, Secp256k1.PublicKey(Key, true), AccountId(Address), destinationId, signature);
            var txId = Hex.Encode(HashWithPrefix(new byte[] { 0x54, 0x58, 0x4E, 0x00 }, blob)).ToUpperInvariant();

            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = destination,
                Amount = Units.FromRaw(sendDrops, 6),
                Fee = Units.FromRaw(fee, 6),
                FeeAsset = Native,
                TxId = txId,
                Payload = Hex.Encode(blob).ToUpperInvariant(),
            };
            if (tag != null) transfer.Notes.Add("Destination tag " + tag.Value);
            transfer.Notes.Add("Confirm within about a minute; after that it expires");
            return transfer;
        }

        private static byte[] AccountId(string address)
        {
            byte[] id;
            TryDecodeAddress(address, out id);
            return id;
        }

        private static byte[] HashWithPrefix(byte[] prefix, byte[] data)
        {
            using (var sha = SHA512.Create()) return Bytes.Take(sha.ComputeHash(Bytes.Concat(prefix, data)), 32);
        }

        public static byte[] Serialize(ulong drops, ulong fee, uint sequence, uint lastLedger, uint? tag, byte[] publicKey, byte[] account, byte[] destination, byte[] signature)
        {
            var parts = new List<byte[]>();
            parts.Add(new byte[] { 0x12, 0x00, 0x00 });
            parts.Add(Bytes.Concat(new byte[] { 0x22 }, Bytes.UInt32BE(0x80000000)));
            parts.Add(Bytes.Concat(new byte[] { 0x24 }, Bytes.UInt32BE(sequence)));
            if (tag != null) parts.Add(Bytes.Concat(new byte[] { 0x2E }, Bytes.UInt32BE(tag.Value)));
            parts.Add(Bytes.Concat(new byte[] { 0x20, 0x1B }, Bytes.UInt32BE(lastLedger)));
            parts.Add(Bytes.Concat(new byte[] { 0x61 }, Amount(drops)));
            parts.Add(Bytes.Concat(new byte[] { 0x68 }, Amount(fee)));
            parts.Add(Bytes.Concat(new byte[] { 0x73, (byte)publicKey.Length }, publicKey));
            if (signature != null) parts.Add(Bytes.Concat(new byte[] { 0x74, (byte)signature.Length }, signature));
            parts.Add(Bytes.Concat(new byte[] { 0x81, 0x14 }, account));
            parts.Add(Bytes.Concat(new byte[] { 0x83, 0x14 }, destination));
            return Bytes.Concat(parts.ToArray());
        }

        private static byte[] Amount(ulong drops)
        {
            var value = 0x4000000000000000UL | drops;
            var bytes = Bytes.UInt64LE(value);
            Array.Reverse(bytes);
            return bytes;
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var result = await CallAsync("submit", new { tx_blob = (string)transfer.Payload }, ct);
            var code = result.Has("engine_result") ? result["engine_result"].AsString() : "";
            if (code != "tesSUCCESS" && code != "terQUEUED")
                throw new WalletException("The network rejected the transaction: " + (result.Has("engine_result_message") ? result["engine_result_message"].AsString() : code));
            return transfer.TxId;
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var result = await CallAsync("tx", new { transaction = txId, binary = false }, ct);
                JsonValue error;
                if (result.TryGet("error", out error)) return TxStage.Pending;
                if (!result.Has("validated") || !result["validated"].IsTrue) return TxStage.Pending;
                var outcome = result["meta"]["TransactionResult"].AsString();
                return outcome == "tesSUCCESS" ? TxStage.Final : TxStage.Failed;
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
                return TxStage.Unknown;
            }
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, 6);
                var result = await CallAsync("account_tx", new { account = Address, limit = 15, ledger_index_min = -1, ledger_index_max = -1 }, ct);
                foreach (var item in result["transactions"].Items)
                {
                    var tx = item.Has("tx") ? item["tx"] : item["tx_json"];
                    if (tx["TransactionType"].AsString() != "Payment" || tx["Destination"].AsString() != Address) continue;
                    var meta = item["meta"];
                    var delivered = meta.Has("delivered_amount") ? meta["delivered_amount"] : tx["Amount"];
                    if (!delivered.IsString || BigInteger.Parse(delivered.AsString()) != raw) continue;
                    var hash = tx.Has("hash") ? tx["hash"].AsString() : item["hash"].AsString();
                    return new IncomingTransfer(hash, tx["Account"].AsString(), amount);
                }
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
            }
            return null;
        }
    }
}
