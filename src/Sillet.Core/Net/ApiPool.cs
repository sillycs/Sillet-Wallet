using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;

namespace Sillet.Net
{
    public sealed class ApiPool
    {
        private readonly string _chainId;
        private readonly string[] _defaults;
        private int _preferred;

        public ApiPool(string chainId, params string[] urls)
        {
            _chainId = chainId;
            _defaults = urls;
        }

        private string[] Urls
        {
            get { return Endpoints.For(_chainId, _defaults); }
        }

        public Task<JsonValue> GetAsync(string path, CancellationToken ct)
        {
            return RunAsync(url => Http.GetJsonAsync(url + path, ct), ct);
        }

        public Task<JsonValue> PostAsync(string path, object body, CancellationToken ct)
        {
            return RunAsync(url => Http.PostJsonAsync(url + path, body, ct), ct);
        }

        private async Task<JsonValue> RunAsync(Func<string, Task<JsonValue>> call, CancellationToken ct)
        {
            var urls = Urls;
            Exception last = null;
            for (int attempt = 0; attempt < urls.Length; attempt++)
            {
                int index = (_preferred + attempt) % urls.Length;
                try
                {
                    var result = await call(urls[index]).ConfigureAwait(false);
                    _preferred = index;
                    return result;
                }
                catch (Exception e) when ((e is HttpRequestException || e is TaskCanceledException || e is WalletException) && !ct.IsCancellationRequested)
                {
                    last = e;
                }
            }
            throw new WalletException("Could not reach the network. Check your connection and try again", last);
        }
    }
}
