using System;
using System.IO;

namespace Sillet.Storage
{
    public static class LocalData
    {
        private const string Prefix = "SILLET-LOCAL-1:";

        public static Func<byte[], byte[]> Protect { get; set; }
        public static Func<byte[], byte[]> Unprotect { get; set; }

        public static string ReadText(string path)
        {
            var text = File.ReadAllText(path);
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return text;
            if (Unprotect == null) throw new InvalidOperationException("Local data is encrypted");
            try
            {
                return System.Text.Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(text.Substring(Prefix.Length).Trim())));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                throw new InvalidOperationException("Local data belongs to another Windows user");
            }
        }

        public static void WriteText(string path, string text)
        {
            if (Protect != null)
            {
                try
                {
                    text = Prefix + Convert.ToBase64String(Protect(System.Text.Encoding.UTF8.GetBytes(text)));
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static bool IsEncrypted(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var reader = new StreamReader(path)) return (reader.ReadLine() ?? "").StartsWith(Prefix, StringComparison.Ordinal);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
