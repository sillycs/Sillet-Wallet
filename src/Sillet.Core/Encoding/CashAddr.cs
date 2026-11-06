using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sillet.Encoding
{
    public static class CashAddr
    {
        public const string Prefix = "bitcoincash";
        private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        private static readonly ulong[] Generator = { 0x98f2bc8e61UL, 0x79b76d99e2UL, 0xf33e5fb3c4UL, 0xae2eabe2a8UL, 0x1e4f43e470UL };

        private static ulong Polymod(IEnumerable<byte> values)
        {
            ulong c = 1;
            foreach (var d in values)
            {
                ulong c0 = c >> 35;
                c = ((c & 0x07ffffffffUL) << 5) ^ d;
                for (int i = 0; i < 5; i++)
                    if (((c0 >> i) & 1) != 0) c ^= Generator[i];
            }
            return c ^ 1;
        }

        private static byte[] ToFiveBits(byte[] data)
        {
            var result = new List<byte>();
            int acc = 0, bits = 0;
            foreach (var b in data)
            {
                acc = (acc << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    result.Add((byte)((acc >> bits) & 31));
                }
            }
            if (bits > 0) result.Add((byte)((acc << (5 - bits)) & 31));
            return result.ToArray();
        }

        private static byte[] ToEightBits(byte[] data)
        {
            var result = new List<byte>();
            int acc = 0, bits = 0;
            foreach (var v in data)
            {
                acc = (acc << 5) | v;
                bits += 5;
                while (bits >= 8)
                {
                    bits -= 8;
                    result.Add((byte)((acc >> bits) & 255));
                }
            }
            if (bits >= 5 || ((acc << (8 - bits)) & 255) != 0) return null;
            return result.ToArray();
        }

        public static string Encode(int type, byte[] hash160, bool withPrefix = true)
        {
            var payload = new byte[21];
            payload[0] = (byte)(type << 3);
            Array.Copy(hash160, 0, payload, 1, 20);
            var data = ToFiveBits(payload);
            var check = Polymod(PrefixValues().Concat(new byte[] { 0 }).Concat(data).Concat(new byte[8]));
            var sb = new StringBuilder(withPrefix ? Prefix + ":" : "");
            foreach (var d in data) sb.Append(Charset[d]);
            for (int i = 7; i >= 0; i--) sb.Append(Charset[(int)((check >> (5 * i)) & 31)]);
            return sb.ToString();
        }

        private static IEnumerable<byte> PrefixValues()
        {
            return Prefix.Select(c => (byte)(c & 31));
        }

        public static bool TryDecode(string text, out int type, out byte[] hash160)
        {
            type = -1;
            hash160 = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.ToLowerInvariant() != text && text.ToUpperInvariant() != text) return false;
            text = text.ToLowerInvariant();
            if (text.StartsWith(Prefix + ":", StringComparison.Ordinal)) text = text.Substring(Prefix.Length + 1);
            else if (text.Contains(":")) return false;
            if (text.Length != 42) return false;
            var values = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                int v = Charset.IndexOf(text[i]);
                if (v < 0) return false;
                values[i] = (byte)v;
            }
            if (Polymod(PrefixValues().Concat(new byte[] { 0 }).Concat(values)) != 0) return false;
            var payload = ToEightBits(values.Take(values.Length - 8).ToArray());
            if (payload == null || payload.Length != 21) return false;
            if ((payload[0] & 7) != 0) return false;
            type = payload[0] >> 3;
            if (type != 0 && type != 1) return false;
            hash160 = payload.Skip(1).ToArray();
            return true;
        }
    }
}
