using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sillet.Crypto;

namespace Sillet.Encoding
{
    public sealed class Base58
    {
        public static readonly Base58 Bitcoin = new Base58("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz");

        private readonly string _alphabet;
        private readonly int[] _map = Enumerable.Repeat(-1, 128).ToArray();

        public Base58(string alphabet)
        {
            if (alphabet.Length != 58) throw new ArgumentException("The alphabet must have 58 characters");
            _alphabet = alphabet;
            for (int i = 0; i < alphabet.Length; i++) _map[alphabet[i]] = i;
        }

        public string Encode(byte[] data)
        {
            int zeros = 0;
            while (zeros < data.Length && data[zeros] == 0) zeros++;

            var digits = new List<byte>(data.Length * 138 / 100 + 1);
            for (int i = zeros; i < data.Length; i++)
            {
                int carry = data[i];
                for (int j = 0; j < digits.Count; j++)
                {
                    carry += digits[j] << 8;
                    digits[j] = (byte)(carry % 58);
                    carry /= 58;
                }
                while (carry > 0)
                {
                    digits.Add((byte)(carry % 58));
                    carry /= 58;
                }
            }

            var sb = new StringBuilder(zeros + digits.Count);
            sb.Append(_alphabet[0], zeros);
            for (int i = digits.Count - 1; i >= 0; i--) sb.Append(_alphabet[digits[i]]);
            return sb.ToString();
        }

        public byte[] Decode(string text)
        {
            int zeros = 0;
            while (zeros < text.Length && text[zeros] == _alphabet[0]) zeros++;

            var bytes = new List<byte>(text.Length);
            for (int i = zeros; i < text.Length; i++)
            {
                char c = text[i];
                int value = c < 128 ? _map[c] : -1;
                if (value < 0) throw new FormatException("Invalid Base58 character: '" + c + "'");
                int carry = value;
                for (int j = 0; j < bytes.Count; j++)
                {
                    carry += bytes[j] * 58;
                    bytes[j] = (byte)(carry & 0xff);
                    carry >>= 8;
                }
                while (carry > 0)
                {
                    bytes.Add((byte)(carry & 0xff));
                    carry >>= 8;
                }
            }

            var result = new byte[zeros + bytes.Count];
            for (int i = 0; i < bytes.Count; i++) result[result.Length - 1 - i] = bytes[i];
            return result;
        }

        public string EncodeCheck(byte[] payload)
        {
            var checksum = Hashes.Sha256d(payload);
            return Encode(Bytes.Concat(payload, Bytes.Take(checksum, 4)));
        }

        public byte[] DecodeCheck(string text)
        {
            byte[] raw;
            try { raw = Decode(text); }
            catch (FormatException) { return null; }
            if (raw.Length < 5) return null;
            var payload = Bytes.Take(raw, raw.Length - 4);
            var checksum = Bytes.Take(Hashes.Sha256d(payload), 4);
            return Bytes.Equal(Bytes.TakeLast(raw, 4), checksum) ? payload : null;
        }
    }
}
