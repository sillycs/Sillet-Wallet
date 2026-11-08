// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.WalletConnect
{
    public sealed class WcPeer
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }

        public string Host
        {
            get
            {
                Uri u;
                return Uri.TryCreate(Url ?? "", UriKind.Absolute, out u) ? u.Host : "";
            }
        }
    }

    // What an app asks for when it wants to connect
    public sealed class WcProposal
    {
        public long Id { get; set; }
        public string PairingTopic { get; set; }
        public WcPeer Peer { get; set; }
        public List<long> RequiredChains { get; } = new List<long>();
        public List<long> OptionalChains { get; } = new List<long>();
        public List<string> RequiredMethods { get; } = new List<string>();
        public List<string> RequiredEvents { get; } = new List<string>();
        public List<string> OptionalMethods { get; } = new List<string>();
        public List<string> OptionalEvents { get; } = new List<string>();
        public bool RequiresOtherNamespaces { get; set; }
        // Solana and Bitcoin style networks, as "solana:..." and "bip122:..."
        public List<string> OtherRequiredChains { get; } = new List<string>();
        public List<string> OtherOptionalChains { get; } = new List<string>();
        public List<string> OtherRequiredMethods { get; } = new List<string>();
        public List<string> OtherOptionalMethods { get; } = new List<string>();
        internal byte[] ProposerPublicKey { get; set; }
    }

    public sealed class WcApproval
    {
        public string Account { get; set; }
        public List<long> Chains { get; } = new List<long>();
        public List<string> Methods { get; } = new List<string>();
        public List<string> Events { get; } = new List<string>();
        public List<string> OtherChains { get; } = new List<string>();
        // The address Sillet gives for each of those networks
        public Dictionary<string, string> OtherAccounts { get; } = new Dictionary<string, string>();
    }

    public sealed class WcSession
    {
        public string Topic { get; set; }
        public string SymKey { get; set; }
        public WcPeer Peer { get; set; }
        public string Account { get; set; }
        public List<long> Chains { get; } = new List<long>();
        public List<string> Methods { get; } = new List<string>();
        public List<string> Events { get; } = new List<string>();
        public long Expiry { get; set; }
        public long CreatedAt { get; set; }
        public List<string> OtherChains { get; } = new List<string>();
        public Dictionary<string, string> OtherAccounts { get; } = new Dictionary<string, string>();
    }

    public sealed class WcRequest
    {
        public WcSession Session { get; set; }
        public long Id { get; set; }
        public string Method { get; set; }
        public long ChainId { get; set; }
        // Full network name such as eip155:1 or solana:5eykt...
        public string Chain { get; set; }
        public JsonValue Params { get; set; }
    }

    // Thrown by the request handler to answer the app with an error
    public sealed class WcRpcException : Exception
    {
        public WcRpcException(int code, string message) : base(message)
        {
            Code = code;
        }

        public int Code { get; }
    }

    // Speaks WalletConnect v2 as the wallet: pairs from a wc: link, answers connection requests and passes signing requests to the owner
    public sealed class WalletConnectService : IDisposable
    {
        public static readonly string[] SupportedMethods = { "personal_sign", "eth_signTypedData", "eth_signTypedData_v4", "eth_sendTransaction", "wallet_switchEthereumChain", "wallet_addEthereumChain" };
        public static readonly string[] SolanaMethods = { "solana_signMessage", "solana_signTransaction", "solana_signAndSendTransaction", "solana_signAllTransactions" };
        public static readonly string[] BitcoinMethods = { "getAccountAddresses", "sendTransfer", "signMessage" };

        public static bool IsSupportedMethod(string method)
        {
            return SupportedMethods.Contains(method) || SolanaMethods.Contains(method) || BitcoinMethods.Contains(method);
        }

        public static readonly string[] SupportedEvents = { "chainChanged", "accountsChanged" };
        private const int SessionDays = 7;
        private const int Ttl = 300;

        private readonly string _projectId;
        private readonly string _storePath;
        private readonly string _relayUrl;
        private readonly object _gate = new object();
        private readonly Dictionary<string, byte[]> _pairings = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, WcSession> _sessions = new Dictionary<string, WcSession>();
        private readonly SemaphoreSlim _askOne = new SemaphoreSlim(1, 1);
        private WcRelay _relay;
        private byte[] _clientSeed;

        public WalletConnectService(string projectId, string storePath, string relayUrl = null)
        {
            _projectId = projectId;
            _storePath = storePath;
            _relayUrl = relayUrl;
        }

        public static bool IsConfigured(string projectId)
        {
            return !string.IsNullOrEmpty(projectId) && projectId.Length == 32 && projectId.All(c => Uri.IsHexDigit(c));
        }

        public static bool IsWcLink(string text)
        {
            return text != null && text.Trim().StartsWith("wc:", StringComparison.OrdinalIgnoreCase);
        }

        // Called when an app asks to connect. Return null to refuse
        public Func<WcProposal, Task<WcApproval>> OnProposal { get; set; }

        // Called for each signing request. Return the JSON result, or throw WcRpcException
        public Func<WcRequest, Task<JsonValue>> OnRequest { get; set; }

        public event Action SessionsChanged;
        public event Action<string> Problem;

        public string AppName { get; set; } = "Sillet";
        public string AppUrl { get; set; } = "https://raton.fun/wallet";
        public string AppDescription { get; set; } = "Open source wallet by the Raton team";

        public IReadOnlyList<WcSession> Sessions
        {
            get { lock (_gate) { return _sessions.Values.OrderBy(s => s.CreatedAt).ToList(); } }
        }

        // ---------- start and storage ----------

        public async Task StartAsync()
        {
            Load();
            if (Sessions.Count == 0) return;
            await EnsureRelayAsync().ConfigureAwait(false);
            foreach (var s in Sessions)
            {
                try { await _relay.SubscribeAsync(s.Topic).ConfigureAwait(false); } catch (Exception) { }
            }
        }

        private void Load()
        {
            lock (_gate)
            {
                if (_clientSeed != null) return;
                _sessions.Clear();
                try
                {
                    if (!string.IsNullOrEmpty(_storePath) && File.Exists(_storePath))
                    {
                        var root = JsonValue.Parse(File.ReadAllText(_storePath));
                        if (root.Has("clientSeed")) _clientSeed = Hex.Decode(root["clientSeed"].AsString());
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        if (root.Has("sessions"))
                            foreach (var j in root["sessions"].Items)
                            {
                                var s = new WcSession
                                {
                                    Topic = j["topic"].AsString(),
                                    SymKey = j["symKey"].AsString(),
                                    Account = j["account"].AsString(),
                                    Expiry = j["expiry"].AsLong(),
                                    CreatedAt = j.Has("createdAt") ? j["createdAt"].AsLong() : 0,
                                    Peer = new WcPeer { Name = j["peerName"].AsString(), Url = j["peerUrl"].AsString(), Description = j.Has("peerDescription") ? j["peerDescription"].AsString() : "" },
                                };
                                foreach (var c in j["chains"].Items) s.Chains.Add(c.AsLong());
                                foreach (var m in j["methods"].Items) s.Methods.Add(m.AsString());
                                foreach (var e in j["events"].Items) s.Events.Add(e.AsString());
                                if (j.Has("others"))
                                    foreach (var o in j["others"].Items)
                                    {
                                        var chain = o["chain"].AsString();
                                        s.OtherChains.Add(chain);
                                        s.OtherAccounts[chain] = o["address"].AsString();
                                    }
                                if (s.Expiry > now) _sessions[s.Topic] = s;
                            }
                    }
                }
                catch (Exception) { _sessions.Clear(); }
                if (_clientSeed == null || _clientSeed.Length != 32) _clientSeed = WcCrypto.RandomBytes(32);
            }
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_storePath)) return;
            try
            {
                List<object> list;
                lock (_gate)
                {
                    list = _sessions.Values.Select(s => (object)new
                    {
                        topic = s.Topic,
                        symKey = s.SymKey,
                        account = s.Account,
                        expiry = s.Expiry,
                        createdAt = s.CreatedAt,
                        peerName = s.Peer.Name ?? "",
                        peerUrl = s.Peer.Url ?? "",
                        peerDescription = s.Peer.Description ?? "",
                        chains = s.Chains,
                        methods = s.Methods,
                        events = s.Events,
                        others = s.OtherChains.Select(c => new { chain = c, address = s.OtherAccounts[c] }).ToList(),
                    }).ToList();
                }
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _storePath + ".tmp";
                File.WriteAllText(tmp, Json.Serialize(new { clientSeed = Hex.Encode(_clientSeed), sessions = list }));
                if (File.Exists(_storePath)) File.Delete(_storePath);
                File.Move(tmp, _storePath);
            }
            catch (Exception) { }
        }

        private async Task EnsureRelayAsync()
        {
            Load();
            lock (_gate)
            {
                if (_relay == null)
                {
                    _relay = new WcRelay(_projectId, _clientSeed, _relayUrl);
                    _relay.MessageReceived += (topic, message, tag) => { var t = OnMessageAsync(topic, message); };
                }
            }
            if (!_relay.IsConnected) await _relay.ConnectAsync().ConfigureAwait(false);
        }

        private void Raise()
        {
            var h = SessionsChanged;
            if (h != null) try { h(); } catch (Exception) { }
        }

        private void Fail(string text)
        {
            var h = Problem;
            if (h != null) try { h(text); } catch (Exception) { }
        }

        // ---------- pairing ----------

        public async Task PairAsync(string uri)
        {
            if (!IsConfigured(_projectId)) throw new WalletException("WalletConnect is not set up in this version");
            var text = (uri ?? "").Trim();
            if (text.Length > 2000 || !text.StartsWith("wc:", StringComparison.OrdinalIgnoreCase)) throw new WalletException("That is not a WalletConnect link");
            var at = text.IndexOf('@');
            var q = text.IndexOf('?');
            if (at < 4 || q < at) throw new WalletException("That is not a WalletConnect link");
            var topic = text.Substring(3, at - 3);
            if (text.Substring(at + 1, q - at - 1) != "2") throw new WalletException("This is an old WalletConnect link (version 1). The app has to update");
            string symKey = null;
            long expiry = 0;
            foreach (var pair in text.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var k = pair.Substring(0, eq);
                var v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                if (k == "symKey") symKey = v;
                else if (k == "expiryTimestamp") long.TryParse(v, out expiry);
            }
            if (topic.Length != 64 || symKey == null || symKey.Length != 64 || !topic.All(Uri.IsHexDigit) || !symKey.All(Uri.IsHexDigit)) throw new WalletException("That is not a WalletConnect link");
            if (expiry > 0 && expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new WalletException("This link has expired. Ask the app for a new one");
            await EnsureRelayAsync().ConfigureAwait(false);
            lock (_gate) { _pairings[topic.ToLowerInvariant()] = Hex.Decode(symKey); }
            await _relay.SubscribeAsync(topic.ToLowerInvariant()).ConfigureAwait(false);
        }

        // ---------- messages ----------

        private static WcPeer PeerFrom(JsonValue meta)
        {
            Func<string, int, string> clean = (s, n) =>
            {
                s = s ?? "";
                var chars = s.Where(c => c >= ' ' && c != '<' && c != '>' && c != (char)127).ToArray();
                var r = new string(chars);
                return r.Length > n ? r.Substring(0, n) : r;
            };
            Func<string, string> get = k => { JsonValue v; return meta != null && meta.TryGet(k, out v) && v.IsString ? v.AsString() : ""; };
            return new WcPeer { Name = clean(get("name"), 40), Url = clean(get("url"), 200), Description = clean(get("description"), 200) };
        }

        private static void Chains(JsonValue ns, List<long> into)
        {
            JsonValue chains;
            if (ns == null || !ns.TryGet("chains", out chains) || !chains.IsArray) return;
            foreach (var c in chains.Items)
            {
                var s = c.IsString ? c.AsString() : "";
                long id;
                if (s.StartsWith("eip155:", StringComparison.Ordinal) && long.TryParse(s.Substring(7), out id)) into.Add(id);
            }
        }

        private static string SpaceOf(string key)
        {
            var i = key.IndexOf(':');
            return i < 0 ? key : key.Substring(0, i);
        }

        private static void OtherChains(string key, JsonValue ns, List<string> into)
        {
            if (key.IndexOf(':') > 0) into.Add(key);
            JsonValue chains;
            if (ns == null || !ns.TryGet("chains", out chains) || !chains.IsArray) return;
            foreach (var c in chains.Items)
                if (c.IsString) into.Add(c.AsString());
        }

        private static void Strings(JsonValue ns, string key, List<string> into)
        {
            JsonValue list;
            if (ns == null || !ns.TryGet(key, out list) || !list.IsArray) return;
            foreach (var m in list.Items) if (m.IsString) into.Add(m.AsString());
        }

        private async Task OnMessageAsync(string topic, string message)
        {
            try
            {
                byte[] pairKey = null;
                WcSession session = null;
                lock (_gate)
                {
                    _pairings.TryGetValue(topic, out pairKey);
                    _sessions.TryGetValue(topic, out session);
                }
                if (pairKey != null)
                {
                    var json = JsonValue.Parse(System.Text.Encoding.UTF8.GetString(WcCrypto.Decrypt(pairKey, message)));
                    await OnPairingMessageAsync(topic, pairKey, json).ConfigureAwait(false);
                }
                else if (session != null)
                {
                    var json = JsonValue.Parse(System.Text.Encoding.UTF8.GetString(WcCrypto.Decrypt(Hex.Decode(session.SymKey), message)));
                    await OnSessionMessageAsync(session, json).ConfigureAwait(false);
                }
            }
            catch (Exception e) { Fail(e.Message); }
        }

        private async Task ReplyAsync(string topic, byte[] key, string id, string resultJson, int tag, string errorJson = null)
        {
            var body = "{\"id\":" + id + ",\"jsonrpc\":\"2.0\"," + (errorJson != null ? "\"error\":" + errorJson : "\"result\":" + resultJson) + "}";
            await _relay.PublishAsync(topic, WcCrypto.Encrypt(key, System.Text.Encoding.UTF8.GetBytes(body)), tag, Ttl, false).ConfigureAwait(false);
        }

        private static string ErrorJson(int code, string message)
        {
            return Json.Serialize(new { code = code, message = message });
        }

        private async Task OnPairingMessageAsync(string pairingTopic, byte[] pairKey, JsonValue json)
        {
            var method = json.Has("method") ? json["method"].AsString() : "";
            var id = json["id"].ToJson();
            if (method == "wc_pairingPing") { await ReplyAsync(pairingTopic, pairKey, id, "true", 1003).ConfigureAwait(false); return; }
            if (method == "wc_pairingDelete")
            {
                await ReplyAsync(pairingTopic, pairKey, id, "true", 1001).ConfigureAwait(false);
                lock (_gate) { _pairings.Remove(pairingTopic); }
                await _relay.UnsubscribeAsync(pairingTopic).ConfigureAwait(false);
                return;
            }
            if (method != "wc_sessionPropose") return;

            var p = json["params"];
            var proposal = new WcProposal { Id = json["id"].AsLong(), PairingTopic = pairingTopic };
            proposal.Peer = PeerFrom(p["proposer"].Has("metadata") ? p["proposer"]["metadata"] : null);
            proposal.ProposerPublicKey = Hex.Decode(p["proposer"]["publicKey"].AsString());
            if (proposal.ProposerPublicKey.Length != 32) return;
            JsonValue req, opt;
            if (p.TryGet("requiredNamespaces", out req) && req.IsObject)
            {
                foreach (var ns in req.Properties)
                {
                    var space = SpaceOf(ns.Key);
                    if (space == "solana" || space == "bip122")
                    {
                        OtherChains(ns.Key, ns.Value, proposal.OtherRequiredChains);
                        Strings(ns.Value, "methods", proposal.OtherRequiredMethods);
                        continue;
                    }
                    // "eip155" or "eip155:1" both mean EVM networks
                    if (space != "eip155") { proposal.RequiresOtherNamespaces = true; continue; }
                    Chains(ns.Value, proposal.RequiredChains);
                    if (ns.Key.StartsWith("eip155:", StringComparison.Ordinal)) { long single; if (long.TryParse(ns.Key.Substring(7), out single)) proposal.RequiredChains.Add(single); }
                    Strings(ns.Value, "methods", proposal.RequiredMethods);
                    Strings(ns.Value, "events", proposal.RequiredEvents);
                }
            }
            if (p.TryGet("optionalNamespaces", out opt) && opt.IsObject)
            {
                foreach (var ns in opt.Properties)
                {
                    var optSpace = SpaceOf(ns.Key);
                    if (optSpace == "solana" || optSpace == "bip122")
                    {
                        OtherChains(ns.Key, ns.Value, proposal.OtherOptionalChains);
                        Strings(ns.Value, "methods", proposal.OtherOptionalMethods);
                        continue;
                    }
                    if (optSpace != "eip155") continue;
                    Chains(ns.Value, proposal.OptionalChains);
                    if (ns.Key.StartsWith("eip155:", StringComparison.Ordinal)) { long single; if (long.TryParse(ns.Key.Substring(7), out single)) proposal.OptionalChains.Add(single); }
                    Strings(ns.Value, "methods", proposal.OptionalMethods);
                    Strings(ns.Value, "events", proposal.OptionalEvents);
                }
            }

            Func<int, string, Task> reject = (code, text) => ReplyAsync(pairingTopic, pairKey, id, null, 1101, ErrorJson(code, text));
            if (proposal.RequiresOtherNamespaces) { await reject(5100, "Sillet does not support one of the networks this app needs").ConfigureAwait(false); return; }
            var handler = OnProposal;
            if (handler == null) { await reject(5000, "User rejected.").ConfigureAwait(false); return; }

            WcApproval approval;
            await _askOne.WaitAsync().ConfigureAwait(false);
            try { approval = await handler(proposal).ConfigureAwait(false); }
            catch (Exception) { approval = null; }
            finally { _askOne.Release(); }
            if (approval == null || (approval.Chains.Count == 0 && approval.OtherChains.Count == 0)) { await reject(5000, "User rejected.").ConfigureAwait(false); return; }

            // Keys for this session: ours and the app's agree on a secret only the two of us know
            byte[] priv, pub;
            WcCrypto.NewKeyPair(out priv, out pub);
            var symKey = WcCrypto.SharedKey(priv, proposal.ProposerPublicKey);
            var sessionTopic = WcCrypto.TopicOf(symKey);
            var expiry = DateTimeOffset.UtcNow.AddDays(SessionDays).ToUnixTimeSeconds();
            var namespaces = new Dictionary<string, object>();
            if (approval.Chains.Count > 0)
            {
                var accounts = approval.Chains.Select(c => "eip155:" + c + ":" + approval.Account).ToList();
                namespaces["eip155"] = new { chains = approval.Chains.Select(c => "eip155:" + c).ToList(), accounts = accounts, methods = approval.Methods.Where(m => SupportedMethods.Contains(m)).ToList(), events = approval.Events };
            }
            foreach (var space in new[] { "solana", "bip122" })
            {
                var chains = approval.OtherChains.Where(c => c.StartsWith(space + ":", StringComparison.Ordinal)).ToList();
                if (chains.Count == 0) continue;
                var methods = space == "solana" ? SolanaMethods : BitcoinMethods;
                namespaces[space] = new { chains = chains, accounts = chains.Select(c => c + ":" + approval.OtherAccounts[c]).ToList(), methods = approval.Methods.Where(m => methods.Contains(m)).ToList(), events = new string[0] };
            }
            var response = Json.Serialize(new { id = json["id"].AsLong(), jsonrpc = "2.0", result = new { relay = new { protocol = "irn" }, responderPublicKey = Hex.Encode(pub) } });
            var settle = Json.Serialize(new
            {
                id = WcCrypto.PayloadId(),
                jsonrpc = "2.0",
                method = "wc_sessionSettle",
                @params = new
                {
                    relay = new { protocol = "irn" },
                    namespaces = namespaces,
                    controller = new { publicKey = Hex.Encode(pub), metadata = new { name = AppName, description = AppDescription, url = AppUrl, icons = new string[0] } },
                    expiry = expiry,
                },
            });
            var session = new WcSession { Topic = sessionTopic, SymKey = Hex.Encode(symKey), Peer = proposal.Peer, Account = approval.Account, Expiry = expiry, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            session.Chains.AddRange(approval.Chains);
            foreach (var c in approval.OtherChains) { session.OtherChains.Add(c); session.OtherAccounts[c] = approval.OtherAccounts[c]; }
            session.Methods.AddRange(approval.Methods);
            session.Events.AddRange(approval.Events);
            lock (_gate) { _sessions[sessionTopic] = session; }
            try
            {
                await _relay.ApproveSessionAsync(sessionTopic, pairingTopic,
                    WcCrypto.Encrypt(pairKey, System.Text.Encoding.UTF8.GetBytes(response)),
                    WcCrypto.Encrypt(symKey, System.Text.Encoding.UTF8.GetBytes(settle))).ConfigureAwait(false);
            }
            catch (Exception)
            {
                lock (_gate) { _sessions.Remove(sessionTopic); }
                throw;
            }
            Save();
            Raise();
            lock (_gate) { _pairings.Remove(pairingTopic); }
            try { await _relay.UnsubscribeAsync(pairingTopic).ConfigureAwait(false); } catch (Exception) { }
        }

        private async Task OnSessionMessageAsync(WcSession session, JsonValue json)
        {
            if (!json.Has("method")) return; // the answer to something we sent
            var key = Hex.Decode(session.SymKey);
            var method = json["method"].AsString();
            var id = json["id"].ToJson();
            switch (method)
            {
                case "wc_sessionPing": await ReplyAsync(session.Topic, key, id, "true", 1115).ConfigureAwait(false); return;
                case "wc_sessionExtend": await ReplyAsync(session.Topic, key, id, "true", 1107).ConfigureAwait(false); return;
                case "wc_sessionUpdate": await ReplyAsync(session.Topic, key, id, "true", 1105).ConfigureAwait(false); return;
                case "wc_sessionEvent": await ReplyAsync(session.Topic, key, id, "true", 1111).ConfigureAwait(false); return;
                case "wc_sessionDelete":
                    await ReplyAsync(session.Topic, key, id, "true", 1113).ConfigureAwait(false);
                    await ForgetAsync(session.Topic).ConfigureAwait(false);
                    return;
                case "wc_sessionRequest": break;
                default: await ReplyAsync(session.Topic, key, id, null, 1109, ErrorJson(1004, "Unsupported method")).ConfigureAwait(false); return;
            }

            Func<int, string, Task> fail = (code, text) => ReplyAsync(session.Topic, key, id, null, 1109, ErrorJson(code, text));
            if (session.Expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) { await fail(8000, "This connection expired. Connect again").ConfigureAwait(false); await ForgetAsync(session.Topic).ConfigureAwait(false); return; }
            var p = json["params"];
            var rpcMethod = p["request"]["method"].AsString();
            var chain = p.Has("chainId") ? p["chainId"].AsString() : "";
            long chainId = 0;
            if (chain.StartsWith("eip155:", StringComparison.Ordinal))
            {
                if (!long.TryParse(chain.Substring(7), out chainId) || !session.Chains.Contains(chainId))
                { await fail(5100, "This network was not approved for this connection").ConfigureAwait(false); return; }
            }
            else if (!session.OtherChains.Contains(chain))
            { await fail(5100, "This network was not approved for this connection").ConfigureAwait(false); return; }
            if (!session.Methods.Contains(rpcMethod)) { await fail(5201, "This action was not approved for this connection").ConfigureAwait(false); return; }
            var handler = OnRequest;
            if (handler == null) { await fail(4100, "Sillet cannot answer right now").ConfigureAwait(false); return; }

            var request = new WcRequest { Session = session, Id = json["id"].AsLong(), Method = rpcMethod, ChainId = chainId, Chain = chain, Params = p["request"].Has("params") ? p["request"]["params"] : JsonValue.NewArray(new JsonValue[0]) };
            JsonValue result;
            await _askOne.WaitAsync().ConfigureAwait(false);
            try { result = await handler(request).ConfigureAwait(false); }
            catch (WcRpcException e) { _askOne.Release(); await fail(e.Code, e.Message).ConfigureAwait(false); return; }
            catch (WalletException e) { _askOne.Release(); await fail(5000, e.Message).ConfigureAwait(false); return; }
            catch (Exception) { _askOne.Release(); await fail(5000, "Something went wrong").ConfigureAwait(false); return; }
            _askOne.Release();
            await ReplyAsync(session.Topic, key, id, (result ?? JsonValue.Null).ToJson(), 1109).ConfigureAwait(false);
        }

        // ---------- ending sessions ----------

        private async Task ForgetAsync(string topic)
        {
            lock (_gate) { _sessions.Remove(topic); }
            Save();
            Raise();
            if (_relay != null) await _relay.UnsubscribeAsync(topic).ConfigureAwait(false);
        }

        public async Task DisconnectAsync(string topic)
        {
            WcSession s;
            lock (_gate) { _sessions.TryGetValue(topic, out s); }
            if (s == null) return;
            try
            {
                await EnsureRelayAsync().ConfigureAwait(false);
                var body = Json.Serialize(new { id = WcCrypto.PayloadId(), jsonrpc = "2.0", method = "wc_sessionDelete", @params = new { code = 6000, message = "User disconnected." } });
                await _relay.PublishAsync(topic, WcCrypto.Encrypt(Hex.Decode(s.SymKey), System.Text.Encoding.UTF8.GetBytes(body)), 1112, Ttl, false).ConfigureAwait(false);
            }
            catch (Exception) { }
            await ForgetAsync(topic).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_relay != null) _relay.Dispose();
        }
    }
}
