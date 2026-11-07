// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;

namespace Sillet.App.Services
{
    public sealed class ClipboardGuard
    {
        public static readonly TimeSpan SwapWindow = TimeSpan.FromSeconds(2);
        private readonly Func<string, bool> _isAddress;
        private string _last;
        private DateTime _lastTime;
        private string _expected;
        private DateTime _expectedTime;

        public ClipboardGuard(Func<string, bool> isAddress)
        {
            _isAddress = isAddress;
        }

        public bool Enabled { get; set; } = true;

        public void ExpectCopy(string text, DateTime nowUtc)
        {
            _expected = Clean(text);
            _expectedTime = nowUtc;
        }

        public string Observe(string text, DateTime nowUtc)
        {
            var current = Clean(text);
            string warning = null;
            if (Enabled && current.Length > 0 && _isAddress(current))
            {
                if (_expected != null && nowUtc - _expectedTime <= SwapWindow && current != _expected)
                    warning = Loc.F("The address copied from Sillet was replaced right away by {0}. This is how clipboard-stealing malware works", Formatters.ShortAddress(current));
                else if (_last != null && current != _last && _isAddress(_last) && nowUtc - _lastTime <= SwapWindow && (_expected == null || _last == _expected || nowUtc - _expectedTime > SwapWindow))
                    warning = Loc.F("A copied address was swapped for {0} within a second. This is how clipboard-stealing malware works", Formatters.ShortAddress(current));
            }
            if (warning != null) _expected = null;
            _last = current;
            _lastTime = nowUtc;
            return warning;
        }

        private static string Clean(string text)
        {
            return (text ?? "").Trim();
        }
    }
}
