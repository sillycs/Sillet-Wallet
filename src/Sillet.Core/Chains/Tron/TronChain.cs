// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Chains.Evm;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Hardware;
using Sillet.Net;

namespace Sillet.Chains.Tron
{
    public sealed class TronChain : ChainBase
    {
        private static string Api { get { return Endpoints.For("tron", new[] { "https://api.trongrid.io" })[0]; } }
        private const long SunPerBandwidthByte = 1000;
        private const long AccountActivationFee = 1100000;

        public TronChain()
        {
            BuiltIn = new[]
            {
                Native,
                new Asset("USDT", "Tether USD (TRC-20)", 6, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"),
            };
        }

        public override string Id { get { return "tron"; } }
        public override string Name { get { return "TRON"; } }
        public override Asset Native { get; } = new Asset("TRX", "TRON", 6);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        public override string DerivationPath { get { return "m/44'/195'/0'/0/" + Account; } }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveSecp256k1(seed, DerivationPath);
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            var pub = Secp256k1.PublicKey(privateKey, false);
            var hash = Hashes.Keccak256(Bytes.Skip(pub, 1));
            return Base58.Bitcoin.EncodeCheck(Bytes.Concat(new byte[] { 0x41 }, Bytes.Skip(hash, 12)));
        }

        public static byte[] AddressBytes(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            var payload = Base58.Bitcoin.DecodeCheck(address.Trim());
            return payload != null && payload.Length == 21 && payload[0] == 0x41 ? payload : null;
        }

        public override bool IsValidAddress(string address)
        {
            return AddressBytes(address) != null;
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://tronscan.org/#/address/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://tronscan.org/#/transaction/" + txId;
        }

        private static Task<JsonValue> PostAsync(string path, object body, CancellationToken ct)
        {
            return Http.PostJsonAsync(Api + path, body, ct);
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, asset.Decimals);
                if (asset.IsNative)
                {
                    var json = await Http.GetJsonAsync(Api + "/v1/accounts/" + Address + "/transactions?only_to=true&limit=15&order_by=block_timestamp,desc", ct);
                    foreach (var tx in json["data"].Items)
                    {
                        var contract = tx["raw_data"]["contract"][0];
                        if (contract["type"].AsString() != "TransferContract") continue;
                        var value = contract["parameter"]["value"];
                        if (value["amount"].AsLong() != (long)raw) continue;
                        var from = Base58.Bitcoin.EncodeCheck(Hex.Decode(value["owner_address"].AsString()));
                        return new IncomingTransfer(tx["txID"].AsString(), from, amount);
                    }
                }
                else
                {
                    var json = await Http.GetJsonAsync(Api + "/v1/accounts/" + Address + "/transactions/trc20?only_to=true&limit=15&contract_address=" + asset.Contract, ct);
                    foreach (var tx in json["data"].Items)
                        if (BigInteger.Parse(tx["value"].AsString()) == raw)
                            return new IncomingTransfer(tx["transaction_id"].AsString(), tx["from"].AsString(), amount);
                }
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
            }
            return null;
        }

        public override bool SupportsCustomTokens
        {
            get { return true; }
        }

        public override async Task<Asset> LookupTokenAsync(string contract, CancellationToken ct = default(CancellationToken))
        {
            contract = (contract ?? "").Trim();
            if (!IsValidAddress(contract)) throw new WalletException("That is not a valid contract address for TRON");
            if (HasToken(contract)) throw new WalletException("This token is already in your list");
            BigInteger decimals;
            string symbol = "", name = "";
            try
            {
                var d = await ConstantCallAsync(contract, "decimals()", "", ct);
                decimals = EvmAbi.DecodeUint(d["constant_result"][0].AsString());
                var s = await ConstantCallAsync(contract, "symbol()", "", ct);
                symbol = EvmAbi.DecodeString(s["constant_result"][0].AsString());
                var n = await ConstantCallAsync(contract, "name()", "", ct);
                name = EvmAbi.DecodeString(n["constant_result"][0].AsString());
            }
            catch (Exception e) when (e is KeyNotFoundException || e is FormatException || e is ArgumentException || e is IndexOutOfRangeException || e is InvalidOperationException)
            {
                throw new WalletException("That contract is not a standard token (TRC-20)");
            }
            if (decimals > 36) throw new WalletException("That contract is not a standard token (TRC-20)");
            if (symbol.Length == 0) symbol = "TOKEN";
            if (symbol.Length > 12) symbol = symbol.Substring(0, 12);
            if (name.Length == 0) name = symbol;
            if (name.Length > 40) name = name.Substring(0, 40);
            return new Asset(symbol, name, (int)decimals, contract) { IsCustom = true };
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var solid = await Http.PostJsonAsync(Api + "/walletsolidity/gettransactioninfobyid", new { value = txId }, ct);
                if (solid != null && solid.IsObject && solid.Has("id"))
                {
                    JsonValue receipt;
                    if (solid.TryGet("receipt", out receipt) && receipt.IsObject && receipt.Has("result") && receipt["result"].AsString() != "SUCCESS") return TxStage.Failed;
                    return TxStage.Final;
                }
                var info = await PostAsync("/wallet/gettransactioninfobyid", new { value = txId }, ct);
                if (info != null && info.IsObject && info.Has("id")) return TxStage.Confirmed;
                var pool = await PostAsync("/wallet/gettransactionbyid", new { value = txId, visible = true }, ct);
                return pool != null && pool.IsObject && pool.Has("txID") ? TxStage.Pending : TxStage.Unknown;
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
                return TxStage.Unknown;
            }
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            BigInteger raw = asset.IsNative ? await TrxBalanceAsync(Address, ct) : await TokenBalanceAsync(asset, ct);
            return Balance(asset, raw);
        }

        private static async Task<long> TrxBalanceAsync(string address, CancellationToken ct)
        {
            var account = await PostAsync("/wallet/getaccount", new { address = address, visible = true }, ct);
            JsonValue balance;
            return account.TryGet("balance", out balance) ? balance.AsLong() : 0;
        }

        private static async Task<bool> IsActivatedAsync(string address, CancellationToken ct)
        {
            var account = await PostAsync("/wallet/getaccount", new { address = address, visible = true }, ct);
            return account.IsObject && account.Has("address");
        }

        private async Task<BigInteger> TokenBalanceAsync(Asset token, CancellationToken ct)
        {
            var result = await ConstantCallAsync(token.Contract, "balanceOf(address)", Hex.Encode(AbiAddress(Address)), ct);
            return EvmAbi.DecodeUint(result["constant_result"][0].AsString());
        }

        private Task<JsonValue> ConstantCallAsync(string contract, string function, string parameterHex, CancellationToken ct)
        {
            return PostAsync("/wallet/triggerconstantcontract", new
            {
                owner_address = Address,
                contract_address = contract,
                function_selector = function,
                parameter = parameterHex,
                visible = true,
            }, ct);
        }

        private static byte[] AbiAddress(string address)
        {
            var word = new byte[32];
            Buffer.BlockCopy(AddressBytes(address), 1, word, 12, 20);
            return word;
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            var raw = CheckTransfer(asset, to, amount, sendMax);
            var trxBalance = await TrxBalanceAsync(Address, ct);
            var resources = await PostAsync("/wallet/getaccountresource", new { address = Address, visible = true }, ct);
            Func<string, long> res = name =>
            {
                JsonValue v;
                return resources.TryGet(name, out v) ? v.AsLong() : 0;
            };
            long freeBandwidth = Math.Max(res("freeNetLimit") - res("freeNetUsed"), 0);
            long stakedBandwidth = Math.Max(res("NetLimit") - res("NetUsed"), 0);
            Func<long, long> bandwidthFee = bytes => freeBandwidth >= bytes || stakedBandwidth >= bytes ? 0 : bytes * SunPerBandwidthByte;

            var notes = new List<string>();
            long fee;
            JsonValue tx;
            string expectedInHex;

            if (asset.IsNative)
            {
                bool activated = await IsActivatedAsync(to, ct);
                fee = bandwidthFee(270);
                if (!activated)
                {
                    fee += AccountActivationFee;
                    notes.Add("New recipient account: TRON charges ~1.1 TRX to activate it");
                }
                if (sendMax) raw = trxBalance - fee;
                if (raw <= 0) throw new WalletException("Your TRX balance does not cover the fee");
                if (raw + fee > trxBalance) throw Insufficient(Native, raw + fee, trxBalance);

                tx = await PostAsync("/wallet/createtransaction", new { owner_address = Address, to_address = to, amount = (long)raw, visible = true }, ct);
                CheckNodeError(tx);
                var contract = tx["raw_data"]["contract"][0];
                var value = contract["parameter"]["value"];
                if (contract["type"].AsString() != "TransferContract" ||
                    value["amount"].AsLong() != (long)raw ||
                    value["to_address"].AsString() != to ||
                    value["owner_address"].AsString() != Address)
                    throw new WalletException("The node returned a different transaction than requested. Send cancelled for safety");
                expectedInHex = Hex.Encode(AddressBytes(to));
            }
            else
            {
                var tokenBalance = await TokenBalanceAsync(asset, ct);
                if (sendMax) raw = tokenBalance;
                if (raw <= 0) throw new WalletException("You have no " + asset.Symbol + " on TRON");
                if (tokenBalance < raw) throw Insufficient(asset, raw, tokenBalance);

                var parameter = Hex.Encode(Bytes.Concat(AbiAddress(to), EvmAbi.Uint(raw)));
                var simulation = await ConstantCallAsync(asset.Contract, "transfer(address,uint256)", parameter, ct);
                JsonValue simResult, ok;
                if (!simulation.TryGet("result", out simResult) || !simResult.TryGet("result", out ok) || !ok.IsTrue)
                    throw new WalletException("The network rejected the token transfer simulation");
                JsonValue energyUsed;
                long energyNeeded = simulation.TryGet("energy_used", out energyUsed) ? energyUsed.AsLong() : 130000;

                var parameters = await Http.GetJsonAsync(Api + "/wallet/getchainparameters", ct);
                long energyPrice = 210;
                foreach (var p in parameters["chainParameter"].Items)
                {
                    JsonValue key, val;
                    if (p.TryGet("key", out key) && key.AsString() == "getEnergyFee" && p.TryGet("value", out val)) energyPrice = val.AsLong();
                }
                long energyAvailable = Math.Max(res("EnergyLimit") - res("EnergyUsed"), 0);
                long energyBurn = Math.Max(energyNeeded - energyAvailable, 0) * energyPrice;
                fee = energyBurn + bandwidthFee(350);
                long feeLimit = Math.Min(Math.Max(fee * 2, 20000000), 1000000000);
                if (fee > trxBalance)
                    throw new WalletException("To move " + asset.Symbol + " on TRON you need TRX for the fee: ~" + Units.FromRaw(fee, 6) +
                                              " TRX and you have " + Units.FromRaw(trxBalance, 6));
                if (energyNeeded > 100000) notes.Add("Higher fee: the recipient does not hold USDT yet");

                var response = await PostAsync("/wallet/triggersmartcontract", new
                {
                    owner_address = Address,
                    contract_address = asset.Contract,
                    function_selector = "transfer(address,uint256)",
                    parameter = parameter,
                    fee_limit = feeLimit,
                    call_value = 0,
                    visible = true,
                }, ct);
                CheckNodeError(response);
                tx = response["transaction"];
                var value = tx["raw_data"]["contract"][0]["parameter"]["value"];
                var expectedData = Hex.Encode(EvmAbi.Selector("transfer(address,uint256)")) + parameter;
                if (value["data"].AsString() != expectedData ||
                    value["contract_address"].AsString() != asset.Contract ||
                    value["owner_address"].AsString() != Address)
                    throw new WalletException("The node returned a different transaction than requested. Send cancelled for safety");
                expectedInHex = expectedData;
            }

            var rawDataHex = tx["raw_data_hex"].AsString();
            var txId = tx["txID"].AsString();
            var hash = Hashes.Sha256(Hex.Decode(rawDataHex));
            if (Hex.Encode(hash) != txId || rawDataHex.IndexOf(expectedInHex, StringComparison.OrdinalIgnoreCase) < 0)
                throw new WalletException("The transaction received from the node failed verification. Send cancelled for safety");

            object payload = tx;
            if (!IsWatchOnly)
            {
                var signature = SignTxId(hash, Key);
                tx.Set("signature", JsonValue.NewArray(new[] { JsonValue.FromString(signature) }));
                tx.Set("visible", JsonValue.FromBool(true));
            }
            else if (!CanSignOnDevice)
            {
                throw new WalletException("This network can't be signed with your hardware wallet here yet. Send from your hardware wallet app");
            }
            else
            {
                tx.Set("visible", JsonValue.FromBool(true));
                notes.Add("Confirm the payment on your Ledger");
            }

            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = to,
                Amount = Units.FromRaw(raw, asset.Decimals),
                Fee = Units.FromRaw(fee, 6),
                FeeAsset = Native,
                TxId = txId,
                Payload = new TronPending(tx, Hex.Decode(rawDataHex), hash),
            };
            transfer.Notes.AddRange(notes);
            transfer.Notes.Add("Confirm within a minute; after that it expires");
            return transfer;
        }

        public static string SignTxId(byte[] txHash, byte[] privateKey)
        {
            var sig = Secp256k1.Sign(txHash, privateKey);
            return Hex.Encode(Bytes.Concat(sig.R, sig.S, new[] { (byte)(27 + sig.RecoveryId) }));
        }

        public override bool SupportsDeviceSigning { get { return true; } }

        private sealed class TronPending
        {
            public TronPending(JsonValue tx, byte[] raw, byte[] hash)
            {
                Tx = tx;
                Raw = raw;
                Hash = hash;
            }

            public JsonValue Tx { get; }
            public byte[] Raw { get; }
            public byte[] Hash { get; }
        }

        public static string AddressFromPublicKey(byte[] uncompressed65)
        {
            var hash = Hashes.Keccak256(Bytes.Skip(uncompressed65, 1));
            return Base58.Bitcoin.EncodeCheck(Bytes.Concat(new byte[] { 0x41 }, Bytes.Skip(hash, 12)));
        }

        private async Task SignOnLedgerAsync(TronPending pending, CancellationToken ct)
        {
            try
            {
                using (var device = await OpenLedgerAsync())
                {
                    var found = await LedgerTron.GetAddressAsync(device, LedgerTron.DefaultPath, ct);
                    if (found.Address != Address) throw new WalletException("This Ledger has a different TRON address than this wallet");
                    var signature = await LedgerTron.SignAsync(device, LedgerTron.DefaultPath, pending.Raw, ct);
                    var recovered = Secp256k1.Recover(pending.Hash, new Org.BouncyCastle.Math.BigInteger(1, Bytes.Take(signature, 32)),
                        new Org.BouncyCastle.Math.BigInteger(1, Bytes.Take(Bytes.Skip(signature, 32), 32)), signature[64] - 27);
                    if (recovered == null || AddressFromPublicKey(recovered) != Address)
                        throw new WalletException("The Ledger signed something different from what Sillet showed. Nothing was sent");
                    pending.Tx.Set("signature", JsonValue.NewArray(new[] { JsonValue.FromString(Hex.Encode(signature)) }));
                }
            }
            catch (LedgerException e)
            {
                throw new WalletException(e.Message);
            }
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var pending = (TronPending)transfer.Payload;
            if (!pending.Tx.Has("signature")) await SignOnLedgerAsync(pending, ct);
            var result = await PostAsync("/wallet/broadcasttransaction", pending.Tx, ct);
            JsonValue ok;
            if (result.TryGet("result", out ok) && ok.IsTrue) return transfer.TxId;
            throw new WalletException("The network rejected the transaction: " + DescribeError(result));
        }

        private static void CheckNodeError(JsonValue response)
        {
            JsonValue error;
            if (response.TryGet("Error", out error)) throw new WalletException("TRON node error: " + error.AsString());
            JsonValue result;
            if (response.TryGet("result", out result) && result.IsObject && result.Has("code"))
                throw new WalletException("TRON node error: " + DescribeError(result));
        }

        private static string DescribeError(JsonValue json)
        {
            JsonValue codeValue, messageValue;
            var code = json.TryGet("code", out codeValue) ? (codeValue.IsString ? codeValue.AsString() : codeValue.ToJson()) : "";
            if (json.TryGet("message", out messageValue) && messageValue.IsString)
            {
                var message = messageValue.AsString();
                try { message = System.Text.Encoding.UTF8.GetString(Hex.Decode(message)); }
                catch (FormatException) { }
                return (code + " " + message).Trim();
            }
            return string.IsNullOrEmpty(code) ? json.ToJson() : code;
        }
    }
}
