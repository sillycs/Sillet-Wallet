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

    // Connects WalletConnect requests to the wallet's own EVM networks
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

        private async Task<WcApproval> ProposalAsync(WcProposal p)
        {
            if (!_wallet.IsUnlocked || Account == null) return null;
            var required = p.RequiredChains.Distinct().ToList();
            if (required.Any(c => ChainFor(c) == null)) return null; // a network we cannot offer
            if (p.RequiredMethods.Any(m => !WalletConnectService.SupportedMethods.Contains(m))) return null;
            var chains = required.Concat(p.OptionalChains.Where(c => ChainFor(c) != null)).Distinct().ToList();
            if (chains.Count == 0) return null;

            var prompt = new WcConnectPrompt { Peer = p.Peer, Account = Account };
            foreach (var c in chains) prompt.Networks.Add(ChainFor(c).Name);
            prompt.Warnings.Add("Sillet could not check who runs this app. Only connect if you started this from a site you trust");
            if (!await _prompts.ConfirmConnectAsync(prompt).ConfigureAwait(false)) return null;

            var approval = new WcApproval { Account = Account };
            approval.Chains.AddRange(chains);
            var wantedMethods = p.RequiredMethods.Concat(p.OptionalMethods).ToList();
            approval.Methods.AddRange(WalletConnectService.SupportedMethods.Where(m => wantedMethods.Count == 0 || wantedMethods.Contains(m) || m.StartsWith("wallet_", StringComparison.Ordinal)));
            if (approval.Methods.Count == 0) approval.Methods.AddRange(WalletConnectService.SupportedMethods);
            approval.Events.AddRange(WalletConnectService.SupportedEvents.Where(e => p.RequiredEvents.Concat(p.OptionalEvents).Contains(e)));
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

        private static byte[] Reverse(byte[] b)
        {
            var c = (byte[])b.Clone();
            Array.Reverse(c);
            return c;
        }
    }
}
