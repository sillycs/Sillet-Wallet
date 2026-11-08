// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Chains.Evm;
using Sillet.Chains.Solana;
using Sillet.Chains.Utxo;
using Sillet.Crypto;
using Sillet.Core;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.WalletConnect
{
    public sealed class WcConnectPrompt
    {
        public WcPeer Peer { get; set; }
        public string Account { get; set; }
        public List<string> Networks { get; } = new List<string>();
        // One line per address the app will see, such as "Ethereum, Base: 0x1234…abcd"
        public List<string> Details { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class WcMessagePrompt
    {
        public WcPeer Peer { get; set; }
        public string Network { get; set; }
        public bool IsTypedData { get; set; }
        public string Text { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class WcTransactionPrompt
    {
        public WcPeer Peer { get; set; }
        public string Network { get; set; }
        public string To { get; set; }
        public string Amount { get; set; }
        public string Fee { get; set; }
        public string Data { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    // The screens of each app implement this. The bridge never signs anything without a yes from here
    public interface IWcPrompts
    {
        Task<bool> ConfirmConnectAsync(WcConnectPrompt prompt);
        Task<bool> ConfirmMessageAsync(WcMessagePrompt prompt);
        Task<bool> ConfirmTransactionAsync(WcTransactionPrompt prompt);
    }

    // Names of the non EVM networks as apps write them (CAIP-2)
    public static class WcNetworks
    {
        public const string Solana = "solana:5eykt4UsFv8P8NJdTREpY1vzqKqZKvdp";

        // Bitcoin Cash is left out on purpose: it shares the Bitcoin genesis block, so apps cannot tell them apart by this name
        public static readonly Dictionary<string, string> Utxo = new Dictionary<string, string>
        {
            { "bitcoin", "bip122:000000000019d6689c085ae165831e93" },
            { "litecoin", "bip122:12a765e31ffd4059bada1e25190f6e98" },
            { "dogecoin", "bip122:1a91e3dace36e2be3bf030a65679fe82" },
        };
    }

    // Connects WalletConnect requests to the wallet's own networks
    public sealed class WcWalletBridge
    {
        private static readonly BigInteger HugeApproval = BigInteger.One << 200;
        private readonly Func<WalletManager> _walletOf;
        private readonly IWcPrompts _prompts;

        public WcWalletBridge(WalletManager wallet, IWcPrompts prompts, WalletConnectService service) : this(() => wallet, prompts, service)
        {
        }

        // The app may swap the wallet when you change profile, so it is asked for every time
        public WcWalletBridge(Func<WalletManager> wallet, IWcPrompts prompts, WalletConnectService service)
        {
            _walletOf = wallet;
            _prompts = prompts;
            Service = service;
            service.OnProposal = ProposalAsync;
            service.OnRequest = RequestAsync;
        }

        public WalletConnectService Service { get; }

        private WalletManager _wallet
        {
            get { return _walletOf(); }
        }

        private IEnumerable<EvmChain> EvmChains
        {
            get { return _wallet.Chains.OfType<EvmChain>(); }
        }

        private EvmChain ChainFor(long id)
        {
            return EvmChains.FirstOrDefault(c => c.ChainId == id);
        }

        private string Account
        {
            get
            {
                var any = EvmChains.FirstOrDefault();
                return any == null ? null : any.Address;
            }
        }

        // The Solana or Bitcoin style chain behind a network name, only if it can sign here
        private ChainBase OtherChainFor(string caip)
        {
            if (caip == WcNetworks.Solana)
            {
                var sol = _wallet.Chains.OfType<SolanaChain>().FirstOrDefault();
                return sol != null && sol.HasAddress && sol.CanSignDirectly ? sol : null;
            }
            foreach (var pair in WcNetworks.Utxo)
            {
                if (pair.Value != caip) continue;
                var utxo = _wallet.Chains.OfType<UtxoChain>().FirstOrDefault(c => c.Id == pair.Key);
                return utxo != null && utxo.HasAddress && utxo.CanSignDirectly ? utxo : null;
            }
            return null;
        }

        private static string[] Pick(string[] ours, List<string> wanted)
        {
            var picked = ours.Where(m => wanted.Count == 0 || wanted.Contains(m)).ToArray();
            return picked.Length == 0 ? ours : picked;
        }

        private static string ShortAddress(string a)
        {
            return a != null && a.Length > 14 ? a.Substring(0, 8) + "…" + a.Substring(a.Length - 6) : a;
        }

        private async Task<WcApproval> ProposalAsync(WcProposal p)
        {
            if (!_wallet.IsUnlocked || Account == null) return null;
            var required = p.RequiredChains.Distinct().ToList();
            if (required.Any(c => ChainFor(c) == null)) return null; // a network we cannot offer
            var otherRequired = p.OtherRequiredChains.Distinct().ToList();
            if (otherRequired.Any(c => OtherChainFor(c) == null)) return null;
            if (p.RequiredMethods.Concat(p.OtherRequiredMethods).Any(m => !WalletConnectService.IsSupportedMethod(m))) return null;
            var chains = required.Concat(p.OptionalChains.Where(c => ChainFor(c) != null)).Distinct().ToList();
            var others = otherRequired.Concat(p.OtherOptionalChains.Where(c => OtherChainFor(c) != null)).Distinct().ToList();
            if (chains.Count == 0 && others.Count == 0) return null;

            var prompt = new WcConnectPrompt { Peer = p.Peer, Account = chains.Count > 0 ? Account : OtherChainFor(others[0]).Address };
            foreach (var c in chains) prompt.Networks.Add(ChainFor(c).Name);
            if (chains.Count > 0) prompt.Details.Add(string.Join(", ", chains.Select(c => ChainFor(c).Name)) + ": " + ShortAddress(Account));
            foreach (var c in others)
            {
                var other = OtherChainFor(c);
                prompt.Networks.Add(other.Name);
                prompt.Details.Add(other.Name + ": " + ShortAddress(other.Address));
            }
            prompt.Warnings.Add("Sillet could not check who runs this app. Only connect if you started this from a site you trust");
            if (!await _prompts.ConfirmConnectAsync(prompt).ConfigureAwait(false)) return null;

            var approval = new WcApproval { Account = Account };
            approval.Chains.AddRange(chains);
            foreach (var c in others) { approval.OtherChains.Add(c); approval.OtherAccounts[c] = OtherChainFor(c).Address; }
            var wanted = p.RequiredMethods.Concat(p.OptionalMethods).Concat(p.OtherRequiredMethods).Concat(p.OtherOptionalMethods).ToList();
            if (chains.Count > 0)
            {
                approval.Methods.AddRange(WalletConnectService.SupportedMethods.Where(m => wanted.Count == 0 || wanted.Contains(m) || m.StartsWith("wallet_", StringComparison.Ordinal)));
                if (approval.Methods.Count == 0) approval.Methods.AddRange(WalletConnectService.SupportedMethods);
            }
            if (others.Any(c => c.StartsWith("solana:", StringComparison.Ordinal))) approval.Methods.AddRange(Pick(WalletConnectService.SolanaMethods, wanted));
            if (others.Any(c => c.StartsWith("bip122:", StringComparison.Ordinal))) approval.Methods.AddRange(Pick(WalletConnectService.BitcoinMethods, wanted));
            if (chains.Count > 0) approval.Events.AddRange(WalletConnectService.SupportedEvents.Where(e => p.RequiredEvents.Concat(p.OptionalEvents).Contains(e)));
            return approval;
        }

        private static bool LooksLikeAddress(JsonValue v)
        {
            return v != null && v.IsString && EvmAddress.IsValid(v.AsString());
        }

        private static void CheckAccount(WcRequest r, string address)
        {
            if (!string.Equals(address, r.Session.Account, StringComparison.OrdinalIgnoreCase))
                throw new WcRpcException(4100, "This request is for a different account than the one connected");
        }

        private static byte[] ParseMessage(string text)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try { return Hex.Decode(text.Substring(2)); } catch (Exception) { }
            }
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        private static string Printable(byte[] data)
        {
            try
            {
                var text = new UTF8Encoding(false, true).GetString(data);
                if (text.All(c => c >= ' ' || c == '\n' || c == '\r' || c == '\t') && text.Length <= 5000) return text;
            }
            catch (Exception) { }
            return "0x" + Hex.Encode(data);
        }

        private async Task<JsonValue> RequestAsync(WcRequest r)
        {
            if (!_wallet.IsUnlocked) throw new WcRpcException(4100, "Sillet is locked. Unlock it and try again");
            if (r.Chain != null && !r.Chain.StartsWith("eip155:", StringComparison.Ordinal)) return await OtherRequestAsync(r).ConfigureAwait(false);
            var chain = ChainFor(r.ChainId);
            if (chain == null) throw new WcRpcException(5100, "Sillet does not support this network");
            if (!chain.CanSignDirectly) throw new WcRpcException(4100, "This wallet cannot sign requests from apps");
            if (!string.Equals(chain.Address, r.Session.Account, StringComparison.OrdinalIgnoreCase))
                throw new WcRpcException(4100, "The connected account is not the one open in Sillet");

            switch (r.Method)
            {
                case "personal_sign":
                {
                    if (r.Params.Count < 2) throw new WcRpcException(5000, "Invalid request");
                    // Some apps send the address first and some send the message first
                    var first = r.Params[0];
                    var second = r.Params[1];
                    var addrFirst = LooksLikeAddress(first) && !(LooksLikeAddress(second) && !LooksLikeAddress(first));
                    var address = addrFirst ? first.AsString() : second.AsString();
                    var message = addrFirst ? second : first;
                    if (!message.IsString) throw new WcRpcException(5000, "Invalid request");
                    CheckAccount(r, address);
                    var bytes = ParseMessage(message.AsString());
                    var prompt = new WcMessagePrompt { Peer = r.Session.Peer, Network = chain.Name, Text = Printable(bytes) };
                    if (prompt.Text.StartsWith("0x", StringComparison.Ordinal)) prompt.Warnings.Add("This is not readable text. Only sign it if you trust the app and know what it does");
                    if (!await _prompts.ConfirmMessageAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    return JsonValue.FromString(chain.SignPersonalMessage(bytes));
                }
                case "eth_signTypedData":
                case "eth_signTypedData_v4":
                {
                    if (r.Params.Count < 2) throw new WcRpcException(5000, "Invalid request");
                    CheckAccount(r, r.Params[0].AsString());
                    var data = r.Params[1];
                    var typed = Eip712.Parse(data.IsString ? data.AsString() : data.ToJson());
                    var domainChain = typed.DomainChainId();
                    if (domainChain.HasValue && domainChain.Value != r.ChainId) throw new WcRpcException(5000, "This data is for another network and was refused");
                    var prompt = new WcMessagePrompt { Peer = r.Session.Peer, Network = chain.Name, IsTypedData = true };
                    var sb = new StringBuilder();
                    sb.AppendLine(typed.PrimaryType);
                    if (typed.DomainName() != null) sb.AppendLine("App: " + typed.DomainName());
                    if (typed.DomainContract() != null) sb.AppendLine("Contract: " + typed.DomainContract());
                    sb.Append(typed.Message.ToJson());
                    prompt.Text = sb.ToString();
                    if (typed.PrimaryType.IndexOf("Permit", StringComparison.OrdinalIgnoreCase) >= 0)
                        prompt.Warnings.Add("This lets an app spend your tokens without another approval. Only sign it on a site you trust");
                    if (!await _prompts.ConfirmMessageAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    return JsonValue.FromString(chain.SignTypedData(typed));
                }
                case "eth_sendTransaction":
                {
                    if (r.Params.Count < 1 || !r.Params[0].IsObject) throw new WcRpcException(5000, "Invalid request");
                    var tx = r.Params[0];
                    if (tx.Has("from")) CheckAccount(r, tx["from"].AsString());
                    string to = tx.Has("to") && !tx["to"].IsNull ? tx["to"].AsString() : null;
                    var value = tx.Has("value") && !tx["value"].IsNull ? EvmAbi.DecodeUint(tx["value"].AsString()) : BigInteger.Zero;
                    var dataHex = tx.Has("data") && !tx["data"].IsNull ? tx["data"].AsString() : (tx.Has("input") ? tx["input"].AsString() : "0x");
                    var data = Hex.Decode(dataHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? dataHex.Substring(2) : dataHex);
                    BigInteger? gas = null;
                    if (tx.Has("gas") && !tx["gas"].IsNull) gas = EvmAbi.DecodeUint(tx["gas"].AsString());
                    else if (tx.Has("gasLimit") && !tx["gasLimit"].IsNull) gas = EvmAbi.DecodeUint(tx["gasLimit"].AsString());

                    var prepared = await chain.PrepareCallAsync(to, value, data, gas).ConfigureAwait(false);
                    var prompt = new WcTransactionPrompt
                    {
                        Peer = r.Session.Peer,
                        Network = chain.Name,
                        To = to ?? "(creates a new contract)",
                        Amount = prepared.Amount + " " + chain.Native.Symbol,
                        Fee = "~" + prepared.Fee + " " + chain.Native.Symbol,
                        Data = data.Length == 0 ? "" : data.Length + " bytes of data" + (data.Length >= 4 ? " (action 0x" + Hex.Encode(data, 0, 4) + ")" : ""),
                    };
                    // approve(spender, amount) with a huge amount lets the spender take every token later
                    if (data.Length == 68 && Hex.Encode(data, 0, 4) == "095ea7b3" && new BigInteger(Bytes.Concat(Reverse(data.Skip(36).Take(32).ToArray()), new byte[] { 0 })) >= HugeApproval)
                        prompt.Warnings.Add("Unlimited approval: this lets the address take all of this token from you at any time");
                    if (!await _prompts.ConfirmTransactionAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    var sent = await chain.BroadcastAsync(prepared).ConfigureAwait(false);
                    return JsonValue.FromString(sent.TxId);
                }
                case "wallet_switchEthereumChain":
                {
                    if (r.Params.Count < 1 || !r.Params[0].Has("chainId")) throw new WcRpcException(5000, "Invalid request");
                    var id = EvmAbi.DecodeUint(r.Params[0]["chainId"].AsString());
                    if (id <= long.MaxValue && r.Session.Chains.Contains((long)id) && ChainFor((long)id) != null) return JsonValue.Null;
                    throw new WcRpcException(4902, "That network is not part of this connection");
                }
                default:
                    throw new WcRpcException(4200, "Sillet does not support this action");
            }
        }

        // ---------- Solana and Bitcoin style networks ----------

        private async Task<JsonValue> OtherRequestAsync(WcRequest r)
        {
            var chain = OtherChainFor(r.Chain);
            if (chain == null) throw new WcRpcException(5100, "Sillet cannot sign on this network right now");
            string expected;
            if (!r.Session.OtherAccounts.TryGetValue(r.Chain, out expected) || !string.Equals(expected, chain.Address, StringComparison.OrdinalIgnoreCase))
                throw new WcRpcException(4100, "The connected account is not the one open in Sillet");
            var sol = chain as SolanaChain;
            if (sol != null) return await SolanaRequestAsync(r, sol).ConfigureAwait(false);
            return await BitcoinRequestAsync(r, (UtxoChain)chain).ConfigureAwait(false);
        }

        private static string StringParam(WcRequest r, string name)
        {
            JsonValue v;
            if (r.Params == null || !r.Params.IsObject || !r.Params.TryGet(name, out v) || !v.IsString) throw new WcRpcException(5000, "Invalid request");
            return v.AsString();
        }

        private static bool HasParam(WcRequest r, string name)
        {
            JsonValue v;
            return r.Params != null && r.Params.IsObject && r.Params.TryGet(name, out v) && !v.IsNull;
        }

        private static JsonValue Obj(object value)
        {
            return JsonValue.Parse(Json.Serialize(value));
        }

        private static SolanaParsedTransaction ReadSolana(string base64)
        {
            byte[] raw;
            try { raw = Convert.FromBase64String(base64); }
            catch (FormatException) { throw new WcRpcException(5000, "Invalid request"); }
            try
            {
                var tx = SolanaWire.Parse(raw);
                if (tx.SignatureCount != tx.RequiredSignatures) throw new WalletException("The transaction is not valid");
                return tx;
            }
            catch (WalletException e) { throw new WcRpcException(5000, e.Message); }
        }

        private async Task<JsonValue> SolanaRequestAsync(WcRequest r, SolanaChain chain)
        {
            var me = chain.PublicKeyBytes;
            switch (r.Method)
            {
                case "solana_signMessage":
                {
                    if (HasParam(r, "pubkey") && StringParam(r, "pubkey") != chain.Address) throw new WcRpcException(4100, "This request is for a different account than the one connected");
                    byte[] bytes;
                    try { bytes = Base58.Bitcoin.Decode(StringParam(r, "message")); }
                    catch (Exception) { throw new WcRpcException(5000, "Invalid request"); }
                    if (bytes.Length == 0 || bytes.Length > 10000) throw new WcRpcException(5000, "Invalid request");
                    // Signing the bytes of a transaction as a "message" would be the same as signing the transaction
                    if (SolanaWire.LooksLikeMessage(bytes)) throw new WcRpcException(5000, "This looks like a transaction, not a message. Sillet did not sign it");
                    var prompt = new WcMessagePrompt { Peer = r.Session.Peer, Network = chain.Name, Text = Printable(bytes) };
                    if (prompt.Text.StartsWith("0x", StringComparison.Ordinal)) prompt.Warnings.Add("This is not readable text. Only sign it if you trust the app and know what it does");
                    if (!await _prompts.ConfirmMessageAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    return Obj(new { signature = Base58.Bitcoin.Encode(chain.SignBytes(bytes)) });
                }
                case "solana_signTransaction":
                case "solana_signAndSendTransaction":
                case "solana_signAllTransactions":
                {
                    var many = r.Method == "solana_signAllTransactions";
                    var list = new List<SolanaParsedTransaction>();
                    if (many)
                    {
                        JsonValue items;
                        if (!r.Params.IsObject || !r.Params.TryGet("transactions", out items) || !items.IsArray || items.Count == 0 || items.Count > 10) throw new WcRpcException(5000, "Invalid request");
                        foreach (var item in items.Items)
                        {
                            if (!item.IsString) throw new WcRpcException(5000, "Invalid request");
                            list.Add(ReadSolana(item.AsString()));
                        }
                    }
                    else
                    {
                        if (!HasParam(r, "transaction")) throw new WcRpcException(4200, "This app sent an old kind of request that Sillet does not read. The app needs to update");
                        list.Add(ReadSolana(StringParam(r, "transaction")));
                    }
                    var indexes = new List<int>();
                    foreach (var tx in list)
                    {
                        var index = SolanaWire.SignerIndex(tx, me);
                        if (index < 0) throw new WcRpcException(4100, "This transaction does not ask for your signature");
                        indexes.Add(index);
                    }

                    var prompt = new WcTransactionPrompt { Peer = r.Session.Peer, Network = chain.Name };
                    var outgoing = BigInteger.Zero;
                    ulong feeLamports = 0;
                    var recipients = new List<string>();
                    var lines = new List<string>();
                    foreach (var tx in list)
                    {
                        var summary = SolanaWire.Describe(tx, me);
                        outgoing += summary.SolOut;
                        feeLamports += 5000UL * (ulong)tx.RequiredSignatures;
                        foreach (var rec in summary.Recipients) if (!recipients.Contains(rec)) recipients.Add(rec);
                        lines.AddRange(summary.Lines);
                        foreach (var w in summary.Warnings) if (!prompt.Warnings.Contains(w)) prompt.Warnings.Add(w);
                    }
                    prompt.To = recipients.Count == 0 ? "(no SOL goes to another address)" : string.Join(", ", recipients.Take(3)) + (recipients.Count > 3 ? "…" : "");
                    prompt.Amount = outgoing > 0 ? Units.FromRaw(outgoing, 9) + " SOL" : "No SOL leaves your wallet, as far as Sillet can read";
                    prompt.Fee = "~" + Units.FromRaw(feeLamports, 9) + " SOL";
                    var what = new StringBuilder();
                    if (many) what.AppendLine(list.Count + " transactions to sign");
                    foreach (var line in lines.Take(8)) what.AppendLine(line);
                    if (r.Method == "solana_signTransaction") what.AppendLine("This only signs. The app sends it");
                    prompt.Data = what.ToString().TrimEnd();
                    if (!await _prompts.ConfirmTransactionAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");

                    var signed = new List<byte[]>();
                    var signatures = new List<string>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var signature = chain.SignBytes(list[i].Message);
                        signed.Add(SolanaWire.WithSignature(list[i], indexes[i], signature));
                        signatures.Add(Base58.Bitcoin.Encode(signature));
                    }
                    if (many) return Obj(new { transactions = signed.Select(Convert.ToBase64String).ToList() });
                    if (r.Method == "solana_signTransaction") return Obj(new { signature = signatures[0], transaction = Convert.ToBase64String(signed[0]) });
                    var sent = await chain.SendRawAsync(Convert.ToBase64String(signed[0])).ConfigureAwait(false);
                    return Obj(new { signature = sent });
                }
                default:
                    throw new WcRpcException(4200, "Sillet does not support this action");
            }
        }

        private async Task<JsonValue> BitcoinRequestAsync(WcRequest r, UtxoChain chain)
        {
            if (HasParam(r, "account") && !string.Equals(StringParam(r, "account"), chain.Address, StringComparison.OrdinalIgnoreCase))
                throw new WcRpcException(4100, "This request is for a different account than the one connected");
            switch (r.Method)
            {
                case "getAccountAddresses":
                    return JsonValue.Parse("[" + Json.Serialize(new { address = chain.Address, publicKey = chain.PublicKeyHex, intention = "payment" }) + "]");
                case "signMessage":
                {
                    var protocol = HasParam(r, "protocol") ? StringParam(r, "protocol") : "ecdsa";
                    if (protocol != "ecdsa") throw new WcRpcException(4200, "Sillet signs Bitcoin messages the classic way (ecdsa) and this app asked for " + protocol);
                    if (HasParam(r, "address") && !string.Equals(StringParam(r, "address"), chain.Address, StringComparison.OrdinalIgnoreCase))
                        throw new WcRpcException(4100, "This request is for a different address than the one connected");
                    var bytes = System.Text.Encoding.UTF8.GetBytes(StringParam(r, "message"));
                    if (bytes.Length == 0 || bytes.Length > 10000) throw new WcRpcException(5000, "Invalid request");
                    var prompt = new WcMessagePrompt { Peer = r.Session.Peer, Network = chain.Name, Text = Printable(bytes) };
                    if (!await _prompts.ConfirmMessageAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    return Obj(new { address = chain.Address, signature = chain.SignMessageBase64(bytes), messageHash = Hex.Encode(chain.MessageHash(bytes)) });
                }
                case "sendTransfer":
                {
                    var to = StringParam(r, "recipientAddress");
                    JsonValue amountValue;
                    if (!r.Params.TryGet("amount", out amountValue)) throw new WcRpcException(5000, "Invalid request");
                    BigInteger sats;
                    if (!BigInteger.TryParse(amountValue.IsString ? amountValue.AsString() : amountValue.ToJson(), out sats) || sats <= 0 || sats > long.MaxValue)
                        throw new WcRpcException(5000, "Invalid request");
                    var prepared = await chain.PrepareTransferAsync(chain.Native, to, Units.FromRaw(sats, chain.Native.Decimals)).ConfigureAwait(false);
                    var prompt = new WcTransactionPrompt
                    {
                        Peer = r.Session.Peer,
                        Network = chain.Name,
                        To = to,
                        Amount = prepared.Amount + " " + chain.Native.Symbol,
                        Fee = "~" + prepared.Fee + " " + chain.Native.Symbol,
                    };
                    if (HasParam(r, "memo") && StringParam(r, "memo").Length > 0) prompt.Warnings.Add("The app asked to add a note to this payment. Sillet sends it without the note");
                    if (!await _prompts.ConfirmTransactionAsync(prompt).ConfigureAwait(false)) throw new WcRpcException(5000, "User rejected.");
                    var sent = await chain.BroadcastAsync(prepared).ConfigureAwait(false);
                    return Obj(new { txid = sent.TxId });
                }
                default:
                    throw new WcRpcException(4200, "Sillet does not support this action");
            }
        }

        private static byte[] Reverse(byte[] b)
        {
            var c = (byte[])b.Clone();
            Array.Reverse(c);
            return c;
        }
    }
}
