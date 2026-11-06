using System;
using System.Security.Cryptography;
using System.Text;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.App.Services
{
    public static class UpdateKey
    {
        public const string PublicKeyHex = "";

        public static bool IsConfigured
        {
            get { return PublicKeyHex.Length == 64; }
        }

        public static string NotesHash(string notes)
        {
            using (var sha = SHA256.Create()) return Hex.Encode(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(notes ?? "")));
        }

        public static byte[] Message(string version, string sha256, string notes)
        {
            return System.Text.Encoding.UTF8.GetBytes("sillet-update-v1\n" + version + "\n" + sha256 + "\n" + NotesHash(notes));
        }

        public static bool Verify(string version, string sha256, string notes, string signatureBase64)
        {
            return Verify(PublicKeyHex, version, sha256, notes, signatureBase64);
        }

        public static bool Verify(string publicKeyHex, string version, string sha256, string notes, string signatureBase64)
        {
            if (string.IsNullOrEmpty(publicKeyHex) || publicKeyHex.Length != 64 || string.IsNullOrEmpty(signatureBase64) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(sha256)) return false;
            try
            {
                var signature = Convert.FromBase64String(signatureBase64);
                if (signature.Length != 64) return false;
                return Ed25519Keys.Verify(Hex.Decode(publicKeyHex), Message(version, sha256, notes), signature);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
