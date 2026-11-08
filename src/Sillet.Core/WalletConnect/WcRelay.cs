// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.WalletConnect
{
    // A connection to the WalletConnect relay: a mailbox server that passes encrypted messages between an app and the wallet
    public sealed class WcRelay : IDisposable
    {
        public const string DefaultUrl = "wss://relay.walletconnect.org";

        private readonly string _url;
        private readonly string _projectId;
        private readonly byte[] _clientSeed;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonValue>> _pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonValue>>();
        private readonly HashSet<string> _topics = new HashSet<string>();
        private readonly Queue<string> _seen = new Queue<string>();
        private readonly HashSet<string> _seenSet = new HashSet<string>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private ClientWebSocket _ws;
        private Task _loop;
        private bool _disposed;

        public WcRelay(string projectId, byte[] clientSeed, string url = null)
        {
            _projectId = projectId;
            _clientSeed = clientSeed;
            _url = string.IsNullOrEmpty(url) ? DefaultUrl : url.TrimEnd('/');
        }

        // topic, base64 message, tag
        public event Action<string, string, int> MessageReceived;

        public bool IsConnected
        {
            get { var ws = _ws; return ws != null && ws.State == WebSocketState.Open; }
        }

        private long NextId()
        {
            return WcCrypto.PayloadId();
        }

        public async Task ConnectAsync(CancellationToken ct = default(CancellationToken))
        {
            lock (_gate) { if (_loop == null) _loop = Task.Run(() => RunAsync()); }
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!IsConnected)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > until) throw new InvalidOperationException("Could not reach the WalletConnect relay");
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        private async Task RunAsync()
        {
            var delay = 1000;
            while (!_stop.IsCancellationRequested)
            {
                ClientWebSocket ws = null;
                try
                {
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    var auth = WcCrypto.RelayToken(_clientSeed, _url.StartsWith("wss://", StringComparison.Ordinal) || _url.StartsWith("ws://", StringComparison.Ordinal) ? _url : "wss://" + _url);
                    var full = _url + "/?auth=" + Uri.EscapeDataString(auth) + "&projectId=" + Uri.EscapeDataString(_projectId) + "&ua=" + Uri.EscapeDataString("wc-2/dotnet-sillet");
                    await ws.ConnectAsync(new Uri(full), _stop.Token).ConfigureAwait(false);
                    _ws = ws;
                    delay = 1000;
                    // Subscribe again to everything we were listening to
                    string[] topics;
                    lock (_gate) { topics = new List<string>(_topics).ToArray(); }
                    foreach (var t in topics) { var topic = t; var ignored = Task.Run(() => RequestAsync("irn_subscribe", new { topic = topic }, 20000)); }
                    await ReceiveLoopAsync(ws).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception) { }
                finally
                {
                    _ws = null;
                    try { if (ws != null) ws.Dispose(); } catch (Exception) { }
                    foreach (var key in _pending.Keys)
                    {
                        TaskCompletionSource<JsonValue> tcs;
                        if (_pending.TryRemove(key, out tcs)) tcs.TrySetException(new InvalidOperationException("The relay connection was lost"));
                    }
                }
                if (_stop.IsCancellationRequested) break;
                try { await Task.Delay(delay, _stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                delay = Math.Min(delay * 2, 30000);
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws)
        {
            var buffer = new byte[16384];
            var sb = new StringBuilder();
            while (ws.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) { if (sb.Length > 4 * 1024 * 1024) return; continue; }
                var text = sb.ToString();
                sb.Clear();
                try { Handle(JsonValue.Parse(text)); } catch (Exception) { }
            }
        }

        private void Handle(JsonValue m)
        {
            JsonValue method;
            if (m.TryGet("method", out method))
            {
                var id = m.Has("id") ? m["id"].ToJson() : null;
                if (method.AsString() == "irn_subscription")
                {
                    var data = m["params"]["data"];
                    var topic = data["topic"].AsString();
                    var message = data["message"].AsString();
                    var tag = data.Has("tag") ? data["tag"].AsInt() : 0;
                    // acknowledge so the relay does not send it again
                    if (id != null) { var ack = Task.Run(() => SendRawAsync("{\"id\":" + id + ",\"jsonrpc\":\"2.0\",\"result\":true}")); }
                    var key = topic + ":" + Hex.Encode(Hashes.Sha256(System.Text.Encoding.UTF8.GetBytes(message)));
                    lock (_gate)
                    {
                        if (!_seenSet.Add(key)) return;
                        _seen.Enqueue(key);
                        while (_seen.Count > 500) _seenSet.Remove(_seen.Dequeue());
                    }
                    var handler = MessageReceived;
                    if (handler != null) Task.Run(() => { try { handler(topic, message, tag); } catch (Exception) { } });
                }
                return;
            }
            if (m.Has("id"))
            {
                TaskCompletionSource<JsonValue> tcs;
                if (_pending.TryRemove(m["id"].ToJson().Trim('"'), out tcs))
                {
                    if (m.Has("error")) tcs.TrySetException(new InvalidOperationException(m["error"].Has("message") ? m["error"]["message"].AsString() : "The relay refused the request"));
                    else tcs.TrySetResult(m.Has("result") ? m["result"] : JsonValue.Null);
                }
            }
        }

        private async Task SendRawAsync(string text)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) throw new InvalidOperationException("Not connected to the relay");
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false); }
            finally { _sendLock.Release(); }
        }

        private async Task<JsonValue> RequestAsync(string method, object parameters, int timeoutMs = 30000)
        {
            var id = NextId().ToString();
            // the answer is handled on another thread, so what the caller does next can never stop the relay from reading
            var tcs = new TaskCompletionSource<JsonValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            try
            {
                await SendRawAsync("{\"id\":" + id + ",\"jsonrpc\":\"2.0\",\"method\":" + Json.Serialize(method) + ",\"params\":" + Json.Serialize(parameters) + "}").ConfigureAwait(false);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, _stop.Token)).ConfigureAwait(false);
                if (done != tcs.Task) throw new TimeoutException("The relay did not answer");
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                TaskCompletionSource<JsonValue> ignored;
                _pending.TryRemove(id, out ignored);
            }
        }

        public async Task SubscribeAsync(string topic)
        {
            if (!IsConnected) await ConnectAsync().ConfigureAwait(false);
            await RequestAsync("irn_subscribe", new { topic = topic }).ConfigureAwait(false);
            lock (_gate) { _topics.Add(topic); }
        }

        public async Task UnsubscribeAsync(string topic)
        {
            lock (_gate) { _topics.Remove(topic); }
            if (!IsConnected) return;
            try { await RequestAsync("irn_unsubscribe", new { topic = topic }, 8000).ConfigureAwait(false); } catch (Exception) { }
        }

        public async Task PublishAsync(string topic, string message, int tag, int ttlSeconds, bool prompt)
        {
            if (!IsConnected) await ConnectAsync().ConfigureAwait(false);
            await RequestAsync("irn_publish", new { topic = topic, message = message, ttl = ttlSeconds, tag = tag, prompt = prompt }).ConfigureAwait(false);
        }

        // The approval of a session is one call that also subscribes the wallet to the new session on the relay
        public async Task ApproveSessionAsync(string sessionTopic, string pairingTopic, string proposalResponse, string settleRequest)
        {
            if (!IsConnected) await ConnectAsync().ConfigureAwait(false);
            await RequestAsync("wc_approveSession", new { sessionTopic = sessionTopic, pairingTopic = pairingTopic, sessionProposalResponse = proposalResponse, sessionSettlementRequest = settleRequest, ttl = 300 }).ConfigureAwait(false);
            lock (_gate) { _topics.Add(sessionTopic); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stop.Cancel(); } catch (Exception) { }
            try { if (_ws != null) _ws.Dispose(); } catch (Exception) { }
        }
    }
}
