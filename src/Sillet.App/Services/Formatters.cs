// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Globalization;

namespace Sillet.App.Services
{
    public static class Formatters
    {
        public static readonly CultureInfo Display = CultureInfo.GetCultureInfo("en-US");
        public const string Hidden = "••••••";

        public static bool IsClp(string currency)
        {
            return string.Equals(currency, "clp", StringComparison.OrdinalIgnoreCase);
        }

        public static string Fiat(decimal value, string currency)
        {
            var abs = Math.Abs(value);
            var info = Currencies.Get(currency);
            bool clp = info.NoCents;
            string format;
            if (clp) format = abs >= 100 ? "N0" : abs >= 1 ? "N2" : "#,##0.####";
            else format = abs >= 1 || abs == 0 ? "N2" : "#,##0.######";
            var text = abs.ToString(format, Display);
            return (value < 0 ? "-" : "") + info.Symbol + text;
        }

        public static string Compact(decimal value, string currency)
        {
            var prefix = Currencies.Get(currency).Symbol;
            var abs = Math.Abs(value);
            decimal div;
            string suffix;
            if (abs >= 1000000000000m) { div = 1000000000000m; suffix = " T"; }
            else if (abs >= 1000000000m) { div = 1000000000m; suffix = " B"; }
            else if (abs >= 1000000m) { div = 1000000m; suffix = " M"; }
            else { div = 1m; suffix = ""; }
            return prefix + (value / div).ToString(div == 1 ? "N0" : "N2", Display) + suffix;
        }

        public static string Crypto(decimal amount, int maxDecimals = 8)
        {
            if (amount == 0) return "0";
            var rounded = decimal.Round(amount, maxDecimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) > Math.Abs(amount)) rounded -= Math.Sign(amount) * Pow10(-maxDecimals);
            return rounded.ToString("#,##0." + new string('#', maxDecimals), Display);
        }

        private static decimal Pow10(int exponent)
        {
            decimal result = 1;
            for (int i = 0; i < -exponent; i++) result /= 10;
            return result;
        }

        public static string Percent(decimal? value)
        {
            if (!value.HasValue) return "-";
            var v = value.Value;
            return (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("N2", Display) + "%";
        }

        public static bool TryParseAmount(string text, out decimal amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim().Replace(" ", "");
            int lastComma = text.LastIndexOf(','), lastDot = text.LastIndexOf('.');
            if (lastComma > lastDot) text = text.Replace(".", "").Replace(',', '.');
            else if (lastComma >= 0) text = text.Replace(",", "");
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0;
        }

        public static string ShortAddress(string address)
        {
            return address.Length <= 16 ? address : address.Substring(0, 8) + "…" + address.Substring(address.Length - 6);
        }
    }
}
