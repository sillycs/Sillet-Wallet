// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;

namespace Sillet.App.Services
{
    public static class AddressGuard
    {
        public static string FindLookalike(string address, IEnumerable<string> known)
        {
            if (string.IsNullOrEmpty(address) || known == null) return null;
            foreach (var other in known)
            {
                if (string.IsNullOrEmpty(other) || other.Length != address.Length) continue;
                if (string.Equals(other, address, StringComparison.Ordinal)) return null;
            }
            foreach (var other in known)
            {
                if (string.IsNullOrEmpty(other) || other.Length != address.Length) continue;
                int prefix = 0;
                while (prefix < address.Length && Same(address[prefix], other[prefix])) prefix++;
                int suffix = 0;
                while (suffix < address.Length - prefix && Same(address[address.Length - 1 - suffix], other[other.Length - 1 - suffix])) suffix++;
                if (prefix >= 5 && suffix >= 4 && prefix + suffix >= 10) return other;
            }
            return null;
        }

        private static bool Same(char a, char b)
        {
            return a == b || (char.IsLetter(a) && char.IsLetter(b) && char.ToLowerInvariant(a) == char.ToLowerInvariant(b));
        }

        public static string Short(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 18) return address ?? "";
            return address.Substring(0, 8) + "..." + address.Substring(address.Length - 6);
        }
    }
}
