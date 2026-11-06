using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public sealed class SwapException : Exception
    {
        public SwapException(string message) : base(message)
        {
        }
    }

    public sealed class SwapNetwork
    {
        public SwapNetwork(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public string Code { get; }
        public string Name { get; }
    }

    public sealed class SwapQuote
    {
        public SwapQuote(decimal fromAmount, decimal toAmount, decimal rate, decimal? min, decimal? max)
        {
            FromAmount = fromAmount;
            ToAmount = toAmount;
            Rate = rate;
            Min = min;
            Max = max;
        }

        public decimal FromAmount { get; }
        public decimal ToAmount { get; }
        public decimal Rate { get; }
        public decimal? Min { get; }
        public decimal? Max { get; }
    }

    public sealed class SwapOrder
    {
        public SwapOrder(string id, string depositAddress, string depositExtraId, decimal amount, decimal amountTo, string status)
        {
            Id = id;
            DepositAddress = depositAddress;
            DepositExtraId = depositExtraId;
            Amount = amount;
            AmountTo = amountTo;
            Status = status;
        }

        public string Id { get; }
        public string DepositAddress { get; }
        public string DepositExtraId { get; }
        public decimal Amount { get; }
        public decimal AmountTo { get; }
        public string Status { get; }
    }

    public static class SwapNetworks
    {
        private static readonly Dictionary<string, string[]> Keywords = new Dictionary<string, string[]>
        {
            { "bitcoin", new[] { "BTC", "BITCOIN" } },
            { "ethereum", new[] { "ETH", "ERC20", "ETHEREUM" } },
            { "tron", new[] { "TRX", "TRC20", "TRON" } },
            { "solana", new[] { "SOL", "SPL", "SOLANA" } },
            { "bsc", new[] { "BSC", "BEP20", "BNB SMART CHAIN", "BNB CHAIN" } },
            { "polygon", new[] { "MATIC", "POLYGON", "POL" } },
            { "arbitrum", new[] { "ARBITRUM", "ARB", "ARBITRUM ONE" } },
            { "optimism", new[] { "OPTIMISM", "OP" } },
            { "base", new[] { "BASE" } },
            { "avalanche", new[] { "AVAXC", "AVAX C", "AVALANCHE C", "C-CHAIN", "CCHAIN" } },
            { "litecoin", new[] { "LTC", "LITECOIN" } },
            { "dogecoin", new[] { "DOGE", "DOGECOIN" } },
            { "kaia", new[] { "KAIA", "KLAY", "KLAYTN" } },
            { "linea", new[] { "LINEA" } },
            { "blast", new[] { "BLAST" } },
            { "gnosis", new[] { "XDAI", "GNOSIS" } },
            { "bitcoincash", new[] { "BCH", "BITCOIN CASH" } },
            { "xrp", new[] { "XRP", "RIPPLE", "XRP LEDGER" } },
            { "ton", new[] { "TON", "TONCOIN", "THE OPEN NETWORK" } },
            { "sui", new[] { "SUI" } },
            { "cosmos", new[] { "ATOM", "COSMOS", "COSMOS HUB" } },
        };

        public static string Match(string chainId, IEnumerable<SwapNetwork> networks)
        {
            string[] words;
            if (!Keywords.TryGetValue(chainId, out words)) return null;
            var list = networks.ToList();
            foreach (var w in words)
            {
                var exact = list.FirstOrDefault(n => string.Equals(n.Code, w, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact.Code;
            }
            foreach (var w in words.Where(w => w.Length > 3))
            {
                var byName = list.FirstOrDefault(n => (n.Name ?? "").ToUpperInvariant().Contains(w));
                if (byName != null) return byName.Code;
            }
            return null;
        }
    }

    public sealed class SwapService
    {
        public const string Provider = "Exolix";
        public const string TermsUrl = "https://exolix.com/terms";
        private const string Api = "https://exolix.com/api/v2";
        private readonly HttpClient _http;
        private readonly Dictionary<string, List<SwapNetwork>> _networks = new Dictionary<string, List<SwapNetwork>>();

        public SwapService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(25) };
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/1.3");
        }

        public async Task<string> NetworkCodeAsync(string symbol, string chainId, CancellationToken ct = default(CancellationToken))
        {
            List<SwapNetwork> list;
            if (!_networks.TryGetValue(symbol, out list))
            {
                var json = await SendAsync(HttpMethod.Get, "/currencies/" + Uri.EscapeDataString(symbol) + "/networks", null, ct);
                var items = json.IsArray ? json : json.Has("data") ? json["data"] : JsonValue.NewArray(new JsonValue[0]);
                list = items.Items.Where(n => n.IsObject)
                    .Select(n => new SwapNetwork(Str(n, "network") ?? "", Str(n, "name") ?? Str(n, "shortName") ?? ""))
                    .Where(n => n.Code.Length > 0).ToList();
                _networks[symbol] = list;
            }
            var code = SwapNetworks.Match(chainId, list);
            if (code == null) throw new SwapException(Loc.F("Swaps for {0} on this network are not available right now", symbol));
            return code;
        }

        public async Task<SwapQuote> QuoteAsync(string fromSymbol, string fromNetwork, string toSymbol, string toNetwork, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            var query = "/rate?coinFrom=" + Uri.EscapeDataString(fromSymbol) + "&networkFrom=" + Uri.EscapeDataString(fromNetwork) +
                        "&coinTo=" + Uri.EscapeDataString(toSymbol) + "&networkTo=" + Uri.EscapeDataString(toNetwork) +
                        "&amount=" + amount.ToString(CultureInfo.InvariantCulture) + "&rateType=float";
            var json = await SendAsync(HttpMethod.Get, query, null, ct, true);
            var to = Dec(json, "toAmount");
            var min = Dec(json, "minAmount");
            var max = Dec(json, "maxAmount");
            if (!to.HasValue || to.Value <= 0)
            {
                if (min.HasValue && amount < min.Value) throw new SwapException(Loc.F("The minimum for this swap is {0}", Formatters.Crypto(min.Value) + " " + fromSymbol));
                var message = Str(json, "message");
                throw new SwapException(string.IsNullOrEmpty(message) ? Loc.T("No rate available for this pair right now") : message);
            }
            return new SwapQuote(Dec(json, "fromAmount") ?? amount, to.Value, Dec(json, "rate") ?? (to.Value / amount), min, max);
        }

        public async Task<SwapOrder> CreateAsync(string fromSymbol, string fromNetwork, string toSymbol, string toNetwork, decimal amount,
            string withdrawalAddress, string refundAddress, CancellationToken ct = default(CancellationToken))
        {
            var body = Json.Serialize(new
            {
                coinFrom = fromSymbol,
                networkFrom = fromNetwork,
                coinTo = toSymbol,
                networkTo = toNetwork,
                amount = amount,
                withdrawalAddress = withdrawalAddress,
                refundAddress = refundAddress,
                rateType = "float",
            });
            var json = await SendAsync(HttpMethod.Post, "/transactions", body, ct);
            var order = ParseOrder(json);
            if (string.IsNullOrEmpty(order.Id) || string.IsNullOrEmpty(order.DepositAddress))
                throw new SwapException(Loc.T("The swap service did not return a deposit address"));
            return order;
        }

        public async Task<SwapOrder> StatusAsync(string id, CancellationToken ct = default(CancellationToken))
        {
            return ParseOrder(await SendAsync(HttpMethod.Get, "/transactions/" + Uri.EscapeDataString(id), null, ct));
        }

        public static SwapOrder ParseOrder(JsonValue json)
        {
            return new SwapOrder(Str(json, "id"), Str(json, "depositAddress"), Str(json, "depositExtraId"),
                Dec(json, "amount") ?? 0, Dec(json, "amountTo") ?? 0, Str(json, "status") ?? "wait");
        }

        public static string StatusText(string status)
        {
            switch ((status ?? "").ToLowerInvariant())
            {
                case "wait": return Loc.T("Waiting for deposit");
                case "confirmation": return Loc.T("Confirming deposit");
                case "confirmed": return Loc.T("Deposit confirmed");
                case "exchanging": return Loc.T("Exchanging");
                case "sending": return Loc.T("Sending to you");
                case "success": return Loc.T("Completed");
                case "overdue": return Loc.T("Expired");
                case "refunded": return Loc.T("Refunded");
                case "refund": return Loc.T("Refunding");
                default: return string.IsNullOrEmpty(status) ? Loc.T("Unknown") : char.ToUpperInvariant(status[0]) + status.Substring(1);
            }
        }

        public static bool IsFinal(string status)
        {
            var s = (status ?? "").ToLowerInvariant();
            return s == "success" || s == "overdue" || s == "refunded";
        }

        private async Task<JsonValue> SendAsync(HttpMethod method, string path, string body, CancellationToken ct, bool allowError = false)
        {
            try
            {
                using (var request = new HttpRequestMessage(method, Api + path))
                {
                    if (body != null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    using (var response = await _http.SendAsync(request, ct))
                    {
                        var text = await response.Content.ReadAsStringAsync();
                        JsonValue json = null;
                        try
                        {
                            json = JsonValue.Parse(text);
                        }
                        catch (FormatException)
                        {
                        }
                        if (response.IsSuccessStatusCode && json != null) return json;
                        if (allowError && json != null && json.IsObject) return json;
                        var message = json != null && json.IsObject ? Str(json, "message") ?? Str(json, "error") : null;
                        throw new SwapException(string.IsNullOrEmpty(message) ? Loc.F("The swap service answered with an error ({0})", (int)response.StatusCode) : MainMessage(message));
                    }
                }
            }
            catch (HttpRequestException)
            {
                throw new SwapException(Loc.T("No connection to the swap service"));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SwapException(Loc.T("The swap service took too long"));
            }
        }

        private static string MainMessage(string message)
        {
            message = message.Trim();
            while (message.EndsWith(".")) message = message.Substring(0, message.Length - 1);
            return message;
        }

        private static string Str(JsonValue e, string name)
        {
            JsonValue v;
            if (e == null || !e.IsObject || !e.TryGet(name, out v) || v.IsNull) return null;
            if (v.IsString) return v.AsString();
            if (v.IsNumber) return v.ToJson();
            return null;
        }

        private static decimal? Dec(JsonValue e, string name)
        {
            JsonValue v;
            decimal d;
            if (e == null || !e.IsObject || !e.TryGet(name, out v)) return null;
            if (v.IsNumber && v.TryDecimal(out d)) return d;
            if (v.IsString && decimal.TryParse(v.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return null;
        }
    }
}
