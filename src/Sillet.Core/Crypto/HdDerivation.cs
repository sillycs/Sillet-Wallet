using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Sillet.Encoding;

namespace Sillet.Crypto
{
    public static class HdDerivation
    {
        private const uint Hardened = 0x80000000;

        public static byte[] DeriveSecp256k1(byte[] seed, string path)
        {
            byte[] chainCode;
            return DeriveSecp256k1Node(seed, path, out chainCode);
        }

        public static byte[] ChildKey(byte[] parentKey, byte[] parentChainCode, uint index)
        {
            if ((index & Hardened) != 0) throw new ArgumentException("Only normal (non hardened) children can be derived here");
            var data = Bytes.Concat(Secp256k1.PublicKey(parentKey, true), Bytes.UInt32BE(index));
            var i = Hashes.HmacSha512(parentChainCode, data);
            var il = Bytes.Take(i, 32);
            if (!Secp256k1.IsValidPrivateKey(il)) throw new CryptographicException("Invalid derivation index");
            return Secp256k1.AddPrivateKeys(parentKey, il);
        }

        public static byte[] DeriveSecp256k1Node(byte[] seed, string path, out byte[] chainCodeOut)
        {
            var master = Hashes.HmacSha512(System.Text.Encoding.ASCII.GetBytes("Bitcoin seed"), seed);
            var key = Bytes.Take(master, 32);
            var chainCode = Bytes.Skip(master, 32);
            if (!Secp256k1.IsValidPrivateKey(key)) throw new CryptographicException("Invalid seed");

            foreach (uint index in ParsePath(path))
            {
                byte[] data = (index & Hardened) != 0
                    ? Bytes.Concat(new byte[] { 0 }, key, Bytes.UInt32BE(index))
                    : Bytes.Concat(Secp256k1.PublicKey(key, true), Bytes.UInt32BE(index));
                var i = Hashes.HmacSha512(chainCode, data);
                var il = Bytes.Take(i, 32);
                if (!Secp256k1.IsValidPrivateKey(il)) throw new CryptographicException("Invalid derivation index");
                var child = Secp256k1.AddPrivateKeys(key, il);
                Array.Clear(key, 0, key.Length);
                key = child;
                chainCode = Bytes.Skip(i, 32);
            }
            chainCodeOut = chainCode;
            return key;
        }

        public static byte[] DeriveEd25519(byte[] seed, string path)
        {
            var i = Hashes.HmacSha512(System.Text.Encoding.ASCII.GetBytes("ed25519 seed"), seed);
            var key = Bytes.Take(i, 32);
            var chainCode = Bytes.Skip(i, 32);
            foreach (uint index in ParsePath(path))
            {
                if ((index & Hardened) == 0) throw new ArgumentException("With ed25519 every level must be hardened (')");
                var data = Bytes.Concat(new byte[] { 0 }, key, Bytes.UInt32BE(index));
                i = Hashes.HmacSha512(chainCode, data);
                Array.Clear(key, 0, key.Length);
                key = Bytes.Take(i, 32);
                chainCode = Bytes.Skip(i, 32);
            }
            return key;
        }

        public static IEnumerable<uint> ParsePath(string path)
        {
            var parts = path.Trim().Split('/');
            if (parts.Length == 0 || parts[0] != "m") throw new ArgumentException("Invalid path: " + path);
            var result = new List<uint>();
            for (int p = 1; p < parts.Length; p++)
            {
                var part = parts[p];
                bool hardened = part.EndsWith("'", StringComparison.Ordinal) || part.EndsWith("h", StringComparison.Ordinal);
                var number = hardened ? part.Substring(0, part.Length - 1) : part;
                uint value;
                if (!uint.TryParse(number, out value) || value >= Hardened) throw new ArgumentException("Invalid path: " + path);
                result.Add(hardened ? value | Hardened : value);
            }
            return result;
        }
    }
}
