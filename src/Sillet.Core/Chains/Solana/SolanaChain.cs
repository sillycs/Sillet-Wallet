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

namespace Sillet.Chains.Solana
{
    public sealed class SolanaChain : ChainBase
    {
        private const ulong BaseFee = 5000;
        private static readonly object Confirmed = new { commitment = "confirmed" };

        private readonly JsonRpcClient _rpc = new JsonRpcClient("solana", new[] { "https://api.mainnet-beta.solana.com", "https://solana-rpc.publicnode.com" });

        public SolanaChain()
        {
            BuiltIn = new[]
            {
                Native,
                new Asset("USDT", "Tether USD (SPL)", 6, "Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB"),
                new Asset("USDC", "USD Coin (SPL)", 6, "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"),
                new Asset("TRUMP", "Official Trump (SPL)", 6, "6p6xgHyF7AeE6TZkSmFsko444wqoP15icUSqi2jfGiPN"),
            };
        }

        public override string Id { get { return "solana"; } }
        public override string Name { get { return "Solana"; } }
        public override Asset Native { get; } = new Asset("SOL", "Solana", 9);
        protected override IReadOnlyList<Asset> BuiltIn { get; }
        public override string DerivationPath { get { return "m/44'/501'/" + Account + "'/0'"; } }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveEd25519(seed, DerivationPath);
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return Base58.Bitcoin.Encode(Ed25519Keys.PublicKey(privateKey));
        }

        public override bool IsValidAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            try { return Base58.Bitcoin.Decode(address).Length == 32; }
            catch (FormatException) { return false; }
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://solscan.io/account/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://solscan.io/tx/" + txId;
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, asset.Decimals);
                var signatures = await _rpc.CallAsync("getSignaturesForAddress", new object[] { Address, new { limit = 8, commitment = "confirmed" } }, ct);
                foreach (var item in signatures.Items)
                {
                    JsonValue err;
                    if (item.TryGet("err", out err) && !err.IsNull) continue;
                    var signature = item["signature"].AsString();
                    var tx = await _rpc.CallAsync("getTransaction", new object[] { signature, new { encoding = "jsonParsed", commitment = "confirmed", maxSupportedTransactionVersion = 0 } }, ct);
                    if (tx.IsNull) continue;
                    var meta = tx["meta"];
                    var keys = tx["transaction"]["message"]["accountKeys"].Items.ToList();
                    BigInteger delta = 0;
                    if (asset.IsNative)
                    {
                        int index = keys.FindIndex(k => k["pubkey"].AsString() == Address);
                        if (index < 0) continue;
                        delta = BigInteger.Parse(meta["postBalances"][index].AsString()) - BigInteger.Parse(meta["preBalances"][index].AsString());
                    }
                    else
                    {
                        Func<JsonValue, BigInteger> sum = list =>
                        {
                            BigInteger total = 0;
                            foreach (var b in list.Items)
                                if (b["mint"].AsString() == asset.Contract && b["owner"].AsString() == Address)
                                    total += BigInteger.Parse(b["uiTokenAmount"]["amount"].AsString());
                            return total;
                        };
                        delta = sum(meta["postTokenBalances"]) - sum(meta["preTokenBalances"]);
                    }
                    if (delta != raw) continue;
                    var sender = keys.Where(k => k["signer"].IsTrue).Select(k => k["pubkey"].AsString()).FirstOrDefault(k => k != Address) ?? "";
                    return new IncomingTransfer(signature, sender, amount);
                }
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
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
            byte[] decoded;
            try
            {
                decoded = Base58.Bitcoin.Decode(contract);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                throw new WalletException("That is not a valid token address for Solana");
            }
            if (decoded == null || decoded.Length != 32) throw new WalletException("That is not a valid token address for Solana");
            if (HasToken(contract)) throw new WalletException("This token is already in your list");
            var info = await _rpc.CallAsync("getAccountInfo", new object[] { contract, new { encoding = "jsonParsed", commitment = "confirmed" } }, ct);
            var value = info["value"];
            if (value.IsNull) throw new WalletException("There is no token at that address on Solana");
            if (value["owner"].AsString() != "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA")
                throw new WalletException("Only standard Solana tokens are supported. This one uses a newer program");
            var parsed = value["data"]["parsed"];
            if (parsed["type"].AsString() != "mint") throw new WalletException("That address is not a token");
            var decimals = parsed["info"]["decimals"].AsInt();
            var symbol = "SPL-" + contract.Substring(0, 4).ToUpperInvariant();
            return new Asset(symbol, "SPL token " + contract.Substring(0, 4) + "…" + contract.Substring(contract.Length - 4), decimals, contract) { IsCustom = true };
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var result = await _rpc.CallAsync("getSignatureStatuses", new object[] { new object[] { txId }, new { searchTransactionHistory = true } }, ct);
                var list = result["value"];
                if (list == null || list.IsNull || list.Count == 0 || list[0].IsNull) return TxStage.Pending;
                var entry = list[0];
                JsonValue error;
                if (entry.TryGet("err", out error) && !error.IsNull) return TxStage.Failed;
                JsonValue level;
                if (entry.TryGet("confirmationStatus", out level) && !level.IsNull && level.AsString() == "finalized") return TxStage.Final;
                return TxStage.Confirmed;
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
                return TxStage.Unknown;
            }
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            if (asset.IsNative) return Balance(asset, await LamportsAsync(Address, ct));
            var result = await _rpc.CallAsync("getTokenAccountsByOwner",
                new object[] { Address, new { mint = asset.Contract }, new { encoding = "jsonParsed", commitment = "confirmed" } }, ct);
            BigInteger total = 0;
            foreach (var account in result["value"].Items)
                total += BigInteger.Parse(account["account"]["data"]["parsed"]["info"]["tokenAmount"]["amount"].AsString());
            return Balance(asset, total);
        }

        private async Task<ulong> LamportsAsync(string address, CancellationToken ct)
        {
            var result = await _rpc.CallAsync("getBalance", new object[] { address, Confirmed }, ct);
            return result["value"].AsULong();
        }

        private async Task<ulong> RentExemptAsync(int dataSize, CancellationToken ct)
        {
            var result = await _rpc.CallAsync("getMinimumBalanceForRentExemption", new object[] { dataSize }, ct);
            return result.AsULong();
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            var raw = CheckTransfer(asset, to, amount, sendMax);
            var me = Base58.Bitcoin.Decode(Address);
            var dest = Base58.Bitcoin.Decode(to);
            var solBalance = await LamportsAsync(Address, ct);
            var instructions = new List<SolanaInstruction>();
            var notes = new List<string>();
            ulong fee = BaseFee;
            ulong sendRaw;

            if (asset.IsNative)
            {
                var rentMin = await RentExemptAsync(0, ct);
                sendRaw = sendMax ? (solBalance > fee ? solBalance - fee : 0) : (ulong)raw;
                if (sendRaw == 0) throw new WalletException("You do not have enough SOL to send");
                if (sendRaw + fee > solBalance) throw Insufficient(Native, sendRaw + fee, solBalance);
                var remaining = solBalance - sendRaw - fee;
                if (remaining > 0 && remaining < rentMin)
                    throw new WalletException("Solana requires keeping at least " + Units.FromRaw(rentMin, 9) + " SOL in the account, or send everything (use \"max\")");
                if (sendRaw < rentMin && await LamportsAsync(to, ct) == 0)
                    throw new WalletException("The recipient account is new: you must send at least " + Units.FromRaw(rentMin, 9) + " SOL");
                instructions.Add(SolanaPrograms.TransferSol(me, dest, sendRaw));
            }
            else
            {
                if (!Ed25519Keys.IsOnCurve(dest))
                    throw new WalletException("That address looks like a token account, not a wallet. Ask the recipient for their main Solana address");
                var mint = Base58.Bitcoin.Decode(asset.Contract);
                var sourceAta = SolanaPrograms.AssociatedTokenAddress(me, mint);
                var destAta = SolanaPrograms.AssociatedTokenAddress(dest, mint);

                BigInteger tokenBalance = 0;
                try
                {
                    var balance = await _rpc.CallAsync("getTokenAccountBalance", new object[] { Base58.Bitcoin.Encode(sourceAta), Confirmed }, ct);
                    tokenBalance = BigInteger.Parse(balance["value"]["amount"].AsString());
                }
                catch (RpcException)
                {
                }
                if (sendMax) raw = tokenBalance;
                if (raw <= 0) throw new WalletException("You have no " + asset.Symbol + " on Solana");
                if (tokenBalance < raw) throw Insufficient(asset, raw, tokenBalance);
                sendRaw = (ulong)raw;

                var info = await _rpc.CallAsync("getAccountInfo", new object[] { Base58.Bitcoin.Encode(destAta), new { encoding = "base64", commitment = "confirmed" } }, ct);
                if (info["value"].IsNull)
                {
                    var rent = await RentExemptAsync(165, ct);
                    fee += rent;
                    instructions.Add(SolanaPrograms.CreateAssociatedTokenAccountIdempotent(me, destAta, dest, mint));
                    notes.Add("Includes ~" + Units.FromRaw(rent, 9) + " SOL to open the recipient's " + asset.Symbol + " account");
                }
                if (fee > solBalance)
                    throw new WalletException("To move " + asset.Symbol + " you need SOL for the fee: ~" + Units.FromRaw(fee, 9) +
                                              " SOL and you have " + Units.FromRaw(solBalance, 9));
                instructions.Add(SolanaPrograms.TransferChecked(sourceAta, mint, destAta, me, sendRaw, (byte)asset.Decimals));
            }

            var latest = await _rpc.CallAsync("getLatestBlockhash", new object[] { new { commitment = "finalized" } }, ct);
            var blockhash = latest["value"]["blockhash"].AsString();
            var message = SolanaTransaction.CompileMessage(me, instructions, Base58.Bitcoin.Decode(blockhash));
            var signed = SolanaTransaction.Sign(message, Key);
            notes.Add("Confirm within a minute; after that it expires");

            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = to,
                Amount = Units.FromRaw(sendRaw, asset.Decimals),
                Fee = Units.FromRaw(fee, 9),
                FeeAsset = Native,
                TxId = Base58.Bitcoin.Encode(signed.Signature),
                Payload = Convert.ToBase64String(signed.Transaction),
            };
            transfer.Notes.AddRange(notes);
            return transfer;
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            try
            {
                var options = new { encoding = "base64", preflightCommitment = "confirmed" };
                var result = await _rpc.CallAsync("sendTransaction", new object[] { (string)transfer.Payload, options }, ct);
                return result.AsString();
            }
            catch (RpcException e)
            {
                throw new WalletException("The network rejected the transaction: " + e.Message);
            }
        }
    }
}
