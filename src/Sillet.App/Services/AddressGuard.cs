using System;
using System.Collections.Generic;

namespace Sillet.App.Services
{
    /// <summary>
    /// Catches address poisoning: scammers send dust from an address that starts and ends like one you used before, hoping you copy it from your history.
    /// Wallets show only the first and last characters, so the fake looks identical. Sillet compares the whole address.
    /// </summary>
    public static class AddressGuard
    {
        /// <summary>Returns the known address that this one imitates, or null. An exact match is the real thing and is never flagged.</summary>
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

        // Addresses on Ethereum style chains ignore letter case in what you see, so 0xAB and 0xab are the same to the eye
        private static bool Same(char a, char b)
        {
            return a == b || (char.IsLetter(a) && char.IsLetter(b) && char.ToLowerInvariant(a) == char.ToLowerInvariant(b));
        }

        /// <summary>First and last characters, the way the lookalike would be shown.</summary>
        public static string Short(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length < 18) return address ?? "";
            return address.Substring(0, 8) + "..." + address.Substring(address.Length - 6);
        }
    }
}
