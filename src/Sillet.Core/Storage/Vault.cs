// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Sillet.Core;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Storage
{
    public static class Vault
    {
        private const int TagBytes = 16;

        public static string DefaultPath
        {
            get { return Path.Combine(DataFolder(), "Sillet", "vault.json"); }
        }

        private static string DataFolder()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(folder)) folder = AppDomain.CurrentDomain.BaseDirectory;
            return Path.GetFullPath(folder);
        }

        public static bool Exists(string path = null)
        {
            return File.Exists(path ?? DefaultPath);
        }

        public const string LocalFormat = "sillet-local-1";
        private const int ScryptN = 131072;
        private const int ScryptR = 8;
        private const int ScryptP = 1;
        private const string LockedMessage = "This wallet file is locked to another Windows user or computer. Restore it with your recovery phrase or an encrypted backup";

        public static Func<byte[], byte[]> LocalProtect { get; set; }
        public static Func<byte[], byte[]> LocalUnprotect { get; set; }
        public static bool LockToDevice { get; set; }

        public static void Save(string mnemonic, string password, string path = null)
        {
            path = path ?? DefaultPath;
            WriteFile(path, Seal(mnemonic, password));
        }

        public static string Seal(string mnemonic, string password)
        {
            if (password == null || password.Length < 8) throw new WalletException("The password must have at least 8 characters");
            var salt = RandomBytes(32);
            var nonce = RandomBytes(12);
            var key = DeriveScrypt(password, salt, ScryptN, ScryptR, ScryptP);
            var plain = System.Text.Encoding.UTF8.GetBytes(mnemonic);
            byte[] sealedBytes;
            try
            {
                sealedBytes = Gcm(true, key, nonce, plain, Header(ScryptN, ScryptR, ScryptP));
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                Array.Clear(plain, 0, plain.Length);
            }
            var cipher = Bytes.Take(sealedBytes, sealedBytes.Length - TagBytes);
            var tag = Bytes.TakeLast(sealedBytes, TagBytes);
            return Json.Serialize(new
            {
                Version = 2,
                Kdf = "scrypt",
                N = ScryptN,
                R = ScryptR,
                P = ScryptP,
                Cipher = "aes-256-gcm",
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(cipher),
                Tag = Convert.ToBase64String(tag),
            });
        }

        public static void WriteFile(string path, string innerJson)
        {
            var text = innerJson;
            if (LockToDevice && LocalProtect != null)
            {
                var data = LocalProtect(System.Text.Encoding.UTF8.GetBytes(innerJson));
                text = Json.Serialize(new { Format = LocalFormat, Data = Convert.ToBase64String(data) });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static string ReadInner(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) throw new WalletException("There is no saved wallet");
            JsonValue file;
            try
            {
                file = JsonValue.Parse(File.ReadAllText(path));
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException)
            {
                throw new WalletException("The wallet file is damaged");
            }
            if (!file.IsObject || !file.Has("Format")) return file.ToJson();
            if (file["Format"].AsString() != LocalFormat) throw new WalletException("The wallet file is damaged");
            if (LocalUnprotect == null) throw new WalletException(LockedMessage);
            try
            {
                return System.Text.Encoding.UTF8.GetString(LocalUnprotect(Convert.FromBase64String(file["Data"].AsString())));
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException || e is FormatException || e is InvalidOperationException)
            {
                throw new WalletException(LockedMessage);
            }
        }

        public static bool IsLockedToDevice(string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                if (!File.Exists(path)) return false;
                var file = JsonValue.Parse(File.ReadAllText(path));
                return file.IsObject && file.Has("Format");
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is IOException)
            {
                return false;
            }
        }

        public static bool NeedsUpgrade(string path = null)
        {
            var file = JsonValue.Parse(ReadInner(path));
            var version = file.Has("Version") ? file["Version"].AsInt() : 1;
            return version < 2 || IsLockedToDevice(path) != (LockToDevice && LocalProtect != null);
        }

        public static void ApplyDeviceLock(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) return;
            WriteFile(path, ReadInner(path));
        }

        public static string Load(string password, string path = null)
        {
            JsonValue file;
            try
            {
                file = JsonValue.Parse(ReadInner(path));
            }
            catch (FormatException)
            {
                throw new WalletException("The wallet file is damaged");
            }
            return Open(file, password);
        }

        public static string Open(JsonValue file, string password)
        {
            byte[] key;
            byte[] aad = null;
            try
            {
                var salt = Convert.FromBase64String(file["Salt"].AsString());
                var kdf = file.Has("Kdf") ? file["Kdf"].AsString() : "pbkdf2-sha256";
                if (kdf == "scrypt")
                {
                    int n = file["N"].AsInt(), r = file["R"].AsInt(), p = file["P"].AsInt();
                    if (n < 16384 || n > 1048576 || (n & (n - 1)) != 0 || r < 1 || r > 32 || p < 1 || p > 16) throw new WalletException("The wallet file is damaged");
                    key = DeriveScrypt(password, salt, n, r, p);
                    aad = Header(n, r, p);
                }
                else if (kdf == "pbkdf2-sha256")
                {
                    var iterations = file["Iterations"].AsInt();
                    if (iterations < 10000 || iterations > 10000000) throw new WalletException("The wallet file is damaged");
                    key = DeriveKey(password, salt, iterations);
                }
                else throw new WalletException("The wallet file is damaged");
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is System.Collections.Generic.KeyNotFoundException || e is OverflowException)
            {
                throw new WalletException("The wallet file is damaged");
            }

            byte[] plain = null;
            try
            {
                var sealedBytes = Bytes.Concat(Convert.FromBase64String(file["Ciphertext"].AsString()), Convert.FromBase64String(file["Tag"].AsString()));
                plain = Gcm(false, key, Convert.FromBase64String(file["Nonce"].AsString()), sealedBytes, aad);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (InvalidCipherTextException)
            {
                throw new WalletException("Wrong password");
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }

        private static byte[] Header(int n, int r, int p)
        {
            return System.Text.Encoding.UTF8.GetBytes("sillet-vault-v2|scrypt|" + n + "|" + r + "|" + p + "|aes-256-gcm");
        }

        private static byte[] DeriveScrypt(string password, byte[] salt, int n, int r, int p)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(password);
            try
            {
                return Org.BouncyCastle.Crypto.Generators.SCrypt.Generate(bytes, salt, n, r, p, 32);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public static void Delete(string path = null)
        {
            path = path ?? DefaultPath;
            if (File.Exists(path)) File.Delete(path);
        }

        public static byte[] Seal(byte[] key, byte[] plain)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("Key must be 32 bytes");
            var nonce = RandomBytes(12);
            var cipher = Gcm(true, key, nonce, plain);
            var result = new byte[nonce.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(cipher, 0, result, nonce.Length, cipher.Length);
            return result;
        }

        public static byte[] Open(byte[] key, byte[] sealedData)
        {
            if (key == null || key.Length != 32 || sealedData == null || sealedData.Length < 12 + TagBytes) throw new WalletException("Invalid sealed data");
            var nonce = new byte[12];
            Buffer.BlockCopy(sealedData, 0, nonce, 0, 12);
            var cipher = new byte[sealedData.Length - 12];
            Buffer.BlockCopy(sealedData, 12, cipher, 0, cipher.Length);
            try
            {
                return Gcm(false, key, nonce, cipher);
            }
            catch (InvalidCipherTextException)
            {
                throw new WalletException("Invalid sealed data");
            }
        }

        private static byte[] Gcm(bool encrypt, byte[] key, byte[] nonce, byte[] input, byte[] aad = null)
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(encrypt, new AeadParameters(new KeyParameter(key), TagBytes * 8, nonce, aad));
            var output = new byte[cipher.GetOutputSize(input.Length)];
            int length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return length == output.Length ? output : Bytes.Take(output, length);
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations)
        {
            using (var kdf = new Rfc2898DeriveBytes(System.Text.Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(32);
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }
    }
}
