// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Text.RegularExpressions;

namespace Sillet.WalletConnect
{
    // Finds the WalletConnect link inside what a web page or another app hands to Sillet:
    //   wc:<topic>@2?...                          the plain link
    //   sillet://wc?uri=<link>                    Sillet's own address (opens the app straight away)
    //   https://raton.fun/wallet/wc?uri=<link>    works from any web page, opens the app if it is installed
    public static class WalletConnectLinks
    {
        private static readonly Regex Plain = new Regex("^wc:[0-9a-fA-F]{64}@2\\?[^\\s]{0,1500}$", RegexOptions.Compiled);

        public static string Extract(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var text = input.Trim();
            if (text.Length > 3000) return null;
            if (text.StartsWith("wc:", StringComparison.OrdinalIgnoreCase)) return Plain.IsMatch(text) ? text : null;

            string rest;
            if (text.StartsWith("sillet://", StringComparison.OrdinalIgnoreCase)) rest = text.Substring(9);
            else if (text.StartsWith("sillet:", StringComparison.OrdinalIgnoreCase)) rest = text.Substring(7);
            else if (text.StartsWith("https://raton.fun/wallet/wc", StringComparison.OrdinalIgnoreCase)) rest = text.Substring(text.IndexOf("/wc", StringComparison.OrdinalIgnoreCase) + 1);
            else return null;

            // "wc:..." right after the address
            rest = rest.TrimStart('/');
            if (rest.StartsWith("wc:", StringComparison.OrdinalIgnoreCase)) return Plain.IsMatch(rest) ? rest : null;

            // "wc?uri=..." or "wc/?uri=..."
            if (!rest.StartsWith("wc", StringComparison.OrdinalIgnoreCase)) return null;
            var q = rest.IndexOf('?');
            if (q < 0) return null;
            foreach (var pair in rest.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0 || pair.Substring(0, eq) != "uri") continue;
                var value = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' ')).Trim();
                return Plain.IsMatch(value) ? value : null;
            }
            return null;
        }

        public static string FromArguments(string[] args)
        {
            if (args == null) return null;
            foreach (var a in args)
            {
                var link = Extract(a);
                if (link != null) return link;
            }
            return null;
        }
    }
}
