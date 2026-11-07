// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sillet.Net
{
    public static class Endpoints
    {
        private static Dictionary<string, string[]> _map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public static readonly string[] Supported =
        {
            "bitcoin", "ethereum", "arbitrum", "optimism", "base", "polygon", "bsc", "avalanche", "kaia", "linea", "blast", "gnosis", "tron", "solana", "litecoin", "bitcoincash", "xrp", "ton", "ton3", "sui", "cosmos",
        };

        public static string[] For(string chainId, string[] defaults)
        {
            string[] custom;
            return chainId != null && _map.TryGetValue(chainId, out custom) && custom.Length > 0 ? custom : defaults;
        }

        public static bool HasCustom(string chainId)
        {
            string[] custom;
            return chainId != null && _map.TryGetValue(chainId, out custom) && custom.Length > 0;
        }

        public static void Load(string text)
        {
            string error;
            _map = Parse(text, out error) ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        public static string Validate(string text)
        {
            string error;
            Parse(text, out error);
            return error;
        }

        private static Dictionary<string, string[]> Parse(string text, out string error)
        {
            error = null;
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return map;
            foreach (var raw in text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var at = line.IndexOfAny(new[] { '=', ' ', '\t' });
                if (at <= 0) { error = line; return null; }
                var id = line.Substring(0, at).Trim().ToLowerInvariant();
                var urls = line.Substring(at + 1).Trim().TrimStart('=').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(u => u.Trim().TrimEnd('/')).ToArray();
                if (!Supported.Contains(id) || urls.Length == 0) { error = line; return null; }
                foreach (var u in urls)
                    if (!IsAllowed(u)) { error = line; return null; }
                map[id] = urls;
            }
            return map;
        }

        private static bool IsAllowed(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            if (uri.Scheme != Uri.UriSchemeHttp) return false;
            var host = uri.Host;
            if (host == "localhost" || host == "127.0.0.1" || host == "::1") return true;
            System.Net.IPAddress ip;
            if (!System.Net.IPAddress.TryParse(host, out ip)) return false;
            var b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31));
        }
    }
}
