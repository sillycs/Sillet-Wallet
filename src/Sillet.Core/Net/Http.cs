using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;

namespace Sillet.Net
{
    public static class Http
    {
        private static readonly object Gate = new object();
        private static string _proxyUrl;
        private static HttpClient _client;

        public static string ProxyUrl
        {
            get { return _proxyUrl; }
            set
            {
                lock (Gate)
                {
                    value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    if (_proxyUrl == value) return;
                    _proxyUrl = value;
                    _client = null;
                }
            }
        }

        public static string ValidateProxy(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) || uri.Port <= 0 || string.IsNullOrEmpty(uri.Host)) return "Use the form socks5://127.0.0.1:9050";
            var scheme = uri.Scheme.ToLowerInvariant();
            if (scheme != "socks5" && scheme != "socks5h" && scheme != "http" && scheme != "https") return "Use the form socks5://127.0.0.1:9050";
            return null;
        }

        public static HttpClient Client
        {
            get
            {
                lock (Gate) return _client ?? (_client = CreateClient());
            }
        }

        public static HttpMessageHandler NewHandler()
        {
            var handler = new HttpClientHandler();
            Uri uri;
            var url = _proxyUrl;
            if (url != null && ValidateProxy(url) == null && Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                var scheme = uri.Scheme.ToLowerInvariant();
                var target = scheme.StartsWith("socks")
                    ? "http://127.0.0.1:" + ProxyBridge.Ensure(uri.Host, uri.Port)
                    : url;
                handler.UseProxy = true;
                handler.Proxy = new WebProxy(target);
            }
            return handler;
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient(NewHandler()) { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/1.0");
            return client;
        }

        public static async Task<JsonValue> GetJsonAsync(string url, CancellationToken ct)
        {
            using (var response = await Client.GetAsync(url, ct).ConfigureAwait(false))
                return await ReadAsync(response, url).ConfigureAwait(false);
        }

        public static async Task<JsonValue> PostJsonAsync(string url, object body, CancellationToken ct)
        {
            using (var content = new StringContent(Json.Serialize(body), System.Text.Encoding.UTF8, "application/json"))
            using (var response = await Client.PostAsync(url, content, ct).ConfigureAwait(false))
                return await ReadAsync(response, url).ConfigureAwait(false);
        }

        public static async Task<string> PostTextAsync(string url, string body, CancellationToken ct)
        {
            using (var content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain"))
            using (var response = await Client.PostAsync(url, content, ct).ConfigureAwait(false))
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new WalletException("The network rejected the operation: " + Trim(text));
                return text.Trim();
            }
        }

        private static async Task<JsonValue> ReadAsync(HttpResponseMessage response, string url)
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new WalletException("Server error (" + (int)response.StatusCode + ") from " + new Uri(url).Host + ": " + Trim(text));
            try
            {
                return JsonValue.Parse(text);
            }
            catch (FormatException e)
            {
                throw new WalletException("Unexpected response from " + new Uri(url).Host + ".", e);
            }
        }

        private static string Trim(string text)
        {
            return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
        }
    }

    public sealed class JsonRpcClient
    {
        private readonly string[] _defaults;
        private readonly string _chainId;
        private int _id;
        private int _preferred;

        public JsonRpcClient(params string[] urls)
        {
            _defaults = urls;
        }

        public JsonRpcClient(string chainId, string[] urls)
        {
            _chainId = chainId;
            _defaults = urls;
        }

        private string[] _urls
        {
            get { return Endpoints.For(_chainId, _defaults); }
        }

        public async Task<JsonValue> CallAsync(string method, object[] parameters, CancellationToken ct = default(CancellationToken))
        {
            Exception last = null;
            for (int attempt = 0; attempt < _urls.Length; attempt++)
            {
                int index = (_preferred + attempt) % _urls.Length;
                var body = new { jsonrpc = "2.0", id = Interlocked.Increment(ref _id), method = method, @params = parameters };
                JsonValue response;
                try
                {
                    response = await Http.PostJsonAsync(_urls[index], body, ct).ConfigureAwait(false);
                }
                catch (Exception e) when ((e is HttpRequestException || e is TaskCanceledException || e is WalletException) && !ct.IsCancellationRequested)
                {
                    last = e;
                    continue;
                }
                _preferred = index;
                JsonValue error;
                if (response.TryGet("error", out error) && !error.IsNull)
                {
                    JsonValue message;
                    var text = error.TryGet("message", out message) && message.IsString ? message.AsString() : error.ToJson();
                    throw new RpcException(text, error);
                }
                return response["result"];
            }
            throw new WalletException("Could not connect to any node on the network. Check your internet connection", last);
        }
    }

    public sealed class RpcException : Exception
    {
        public RpcException(string message, JsonValue error) : base(message)
        {
            Error = error;
        }

        public JsonValue Error { get; }
    }
}
