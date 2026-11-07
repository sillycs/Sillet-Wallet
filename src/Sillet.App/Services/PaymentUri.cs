// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Globalization;

namespace Sillet.App.Services
{
    public sealed class PaymentRequest
    {
        public PaymentRequest(string address, decimal? amount)
        {
            Address = address;
            Amount = amount;
        }

        public string Address { get; }
        public decimal? Amount { get; }
    }

    public static class PaymentUri
    {
        public static PaymentRequest Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = raw.Trim();
            string query = null;
            int q = text.IndexOf('?');
            if (q >= 0)
            {
                query = text.Substring(q + 1);
                text = text.Substring(0, q);
            }

            int colon = text.IndexOf(':');
            if (colon > 0 && colon <= 20 && IsScheme(text.Substring(0, colon))) text = text.Substring(colon + 1);
            if (text.StartsWith("//")) text = text.Substring(2);
            if (text.StartsWith("pay-", StringComparison.OrdinalIgnoreCase)) text = text.Substring(4);
            int at = text.IndexOf('@');
            if (at > 0) text = text.Substring(0, at);
            int slash = text.IndexOf('/');
            if (slash > 0) text = text.Substring(0, slash);
            text = text.Trim();
            if (text.Length == 0) return null;

            decimal? amount = null;
            if (query != null)
            {
                foreach (var part in query.Split('&'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = part.Substring(0, eq).ToLowerInvariant();
                    if (key != "amount") continue;
                    decimal value;
                    var number = Uri.UnescapeDataString(part.Substring(eq + 1));
                    if (decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0) amount = value;
                }
            }
            return new PaymentRequest(text, amount);
        }

        private static bool IsScheme(string value)
        {
            foreach (var c in value)
                if (!char.IsLetter(c) && c != '-' && c != '+') return false;
            return true;
        }
    }
}
