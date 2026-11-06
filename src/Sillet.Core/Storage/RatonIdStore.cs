using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sillet.Storage
{
    internal static class RatonIdStore
    {
        private const int Pieces = 5;
        private const int Needed = 3;
        private const int SecretSize = 16;

        public static byte[] Load(string dataFolder)
        {
            var files = Locations(dataFolder);
            var found = new Dictionary<int, byte[]>();
            for (int i = 0; i < files.Count; i++)
            {
                var piece = Read(files[i]);
                if (piece != null && !found.ContainsKey(piece.Item1)) found[piece.Item1] = piece.Item2;
            }
            byte[] secret;
            if (found.Count >= Needed)
            {
                var points = found.Take(Needed).Select(p => new KeyValuePair<int, byte[]>(p.Key, p.Value)).ToList();
                secret = Combine(points, 0);
                for (int i = 0; i < files.Count; i++)
                {
                    var index = i + 1;
                    if (found.ContainsKey(index) && Matches(files[i], index, found[index])) continue;
                    Write(files[i], index, Combine(points, index));
                }
                return secret;
            }
            secret = new byte[SecretSize];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            var shares = Split(secret);
            for (int i = 0; i < files.Count; i++) Write(files[i], i + 1, shares[i]);
            return secret;
        }

        private static bool Matches(string path, int index, byte[] share)
        {
            var piece = Read(path);
            return piece != null && piece.Item1 == index && piece.Item2.SequenceEqual(share);
        }

        private static List<string> Locations(string dataFolder)
        {
            var list = new List<string>();
            Add(list, dataFolder, "profiles.cache");
            Add(list, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine("Raton", "settings.idx"));
            Add(list, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine("Raton", "sillet.tmpdb"));
            Add(list, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.Combine(".raton", "state.bin"));
            Add(list, dataFolder, Path.Combine("avatars", ".thumbs"));
            int extra = 0;
            while (list.Count < Pieces && extra < 10) Add(list, dataFolder, Path.Combine("cache" + extra++, "index.dat"));
            return list.Take(Pieces).ToList();
        }

        private static void Add(List<string> list, string root, string relative)
        {
            if (string.IsNullOrEmpty(root)) return;
            var path = Path.Combine(root, relative);
            if (!list.Contains(path)) list.Add(path);
        }

        private static byte[] Key(string purpose)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("raton.fun/sillet/store/v1/" + purpose));
        }

        private static void Write(string path, int index, byte[] share)
        {
            try
            {
                var plain = new byte[1 + share.Length];
                plain[0] = (byte)index;
                Buffer.BlockCopy(share, 0, plain, 1, share.Length);
                byte[] iv = new byte[16], cipher;
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(iv);
                using (var aes = Aes.Create())
                {
                    aes.Key = Key("aes");
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var encryptor = aes.CreateEncryptor()) cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                }
                var body = new byte[iv.Length + cipher.Length];
                Buffer.BlockCopy(iv, 0, body, 0, iv.Length);
                Buffer.BlockCopy(cipher, 0, body, iv.Length, cipher.Length);
                byte[] mac;
                using (var hmac = new HMACSHA256(Key("mac"))) mac = hmac.ComputeHash(body);
                var all = new byte[body.Length + mac.Length];
                Buffer.BlockCopy(body, 0, all, 0, body.Length);
                Buffer.BlockCopy(mac, 0, all, body.Length, mac.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, all);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static Tuple<int, byte[]> Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var all = File.ReadAllBytes(path);
                if (all.Length < 16 + 16 + 32 || all.Length > 256) return null;
                var body = new byte[all.Length - 32];
                var mac = new byte[32];
                Buffer.BlockCopy(all, 0, body, 0, body.Length);
                Buffer.BlockCopy(all, body.Length, mac, 0, 32);
                byte[] expected;
                using (var hmac = new HMACSHA256(Key("mac"))) expected = hmac.ComputeHash(body);
                int difference = 0;
                for (int i = 0; i < 32; i++) difference |= expected[i] ^ mac[i];
                if (difference != 0) return null;
                var iv = new byte[16];
                Buffer.BlockCopy(body, 0, iv, 0, 16);
                byte[] plain;
                using (var aes = Aes.Create())
                {
                    aes.Key = Key("aes");
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var decryptor = aes.CreateDecryptor()) plain = decryptor.TransformFinalBlock(body, 16, body.Length - 16);
                }
                if (plain.Length != 1 + SecretSize || plain[0] < 1 || plain[0] > Pieces) return null;
                var share = new byte[SecretSize];
                Buffer.BlockCopy(plain, 1, share, 0, SecretSize);
                return Tuple.Create((int)plain[0], share);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static byte Multiply(byte a, byte b)
        {
            int result = 0, x = a, y = b;
            while (y > 0)
            {
                if ((y & 1) != 0) result ^= x;
                x <<= 1;
                if ((x & 0x100) != 0) x ^= 0x11b;
                y >>= 1;
            }
            return (byte)result;
        }

        private static byte Inverse(byte a)
        {
            byte result = 1, power = a;
            for (int bit = 1; bit < 8; bit++)
            {
                power = Multiply(power, power);
                result = Multiply(result, power);
            }
            return result;
        }

        private static List<byte[]> Split(byte[] secret)
        {
            var shares = Enumerable.Range(0, Pieces).Select(i => new byte[secret.Length]).ToList();
            var coefficients = new byte[Needed - 1];
            using (var random = RandomNumberGenerator.Create())
            {
                for (int position = 0; position < secret.Length; position++)
                {
                    random.GetBytes(coefficients);
                    for (int piece = 0; piece < Pieces; piece++)
                    {
                        byte x = (byte)(piece + 1), value = secret[position], power = 1;
                        for (int c = 0; c < coefficients.Length; c++)
                        {
                            power = Multiply(power, x);
                            value ^= Multiply(coefficients[c], power);
                        }
                        shares[piece][position] = value;
                    }
                }
            }
            return shares;
        }

        private static byte[] Combine(List<KeyValuePair<int, byte[]>> points, int at)
        {
            var result = new byte[SecretSize];
            for (int position = 0; position < SecretSize; position++)
            {
                byte total = 0;
                foreach (var point in points)
                {
                    byte numerator = 1, denominator = 1;
                    foreach (var other in points)
                    {
                        if (other.Key == point.Key) continue;
                        numerator = Multiply(numerator, (byte)(at ^ other.Key));
                        denominator = Multiply(denominator, (byte)(point.Key ^ other.Key));
                    }
                    total ^= Multiply(point.Value[position], Multiply(numerator, Inverse(denominator)));
                }
                result[position] = total;
            }
            return result;
        }
    }
}
