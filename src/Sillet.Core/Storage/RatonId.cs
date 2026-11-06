using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sillet.Storage
{
    public static class RatonId
    {
        public const string Word = "ratonfun";
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const string Prefix = "RTN1";
        private const int RandomChars = 26;
        private const int TagChars = 14;

        private static string _cached;

        public static string Get(string folder = null)
        {
            if (_cached != null) return _cached;
            folder = folder ?? Path.GetDirectoryName(Vault.DefaultPath);
            return _cached = Make(RatonIdStore.Load(folder));
        }

        public static string Make(byte[] random16)
        {
            if (random16 == null || random16.Length != 16) throw new ArgumentException("Sixteen bytes are needed");
            var randomPart = Encode(random16, RandomChars);
            var data = randomPart + Tag(randomPart);
            var builder = new StringBuilder(Prefix);
            for (int group = 0; group < Word.Length; group++)
            {
                builder.Append('-').Append(Word[group]);
                builder.Append(data, group * 5, 5);
            }
            return builder.ToString();
        }

        public static bool Verify(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var parts = id.Trim().Split('-');
            if (parts.Length != Word.Length + 1 || parts[0] != Prefix) return false;
            var data = new StringBuilder();
            for (int group = 0; group < Word.Length; group++)
            {
                var part = parts[group + 1];
                if (part.Length != 6 || part[0] != Word[group]) return false;
                for (int i = 1; i < 6; i++)
                    if (Alphabet.IndexOf(part[i]) < 0) return false;
                data.Append(part, 1, 5);
            }
            var text = data.ToString();
            var randomPart = text.Substring(0, RandomChars);
            return Tag(randomPart) == text.Substring(RandomChars);
        }

        private static string Tag(string randomPart)
        {
            byte[] key;
            using (var sha = SHA256.Create()) key = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("raton.fun/sillet/id/v1"));
            using (var hmac = new HMACSHA256(key))
            {
                var mac = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Word + "|1|" + randomPart));
                var part = new byte[9];
                Buffer.BlockCopy(mac, 0, part, 0, 9);
                return Encode(part, TagChars);
            }
        }

        private static string Encode(byte[] bytes, int chars)
        {
            var builder = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (var b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5 && builder.Length < chars)
                {
                    builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0 && builder.Length < chars) builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return builder.ToString();
        }
    }
}
