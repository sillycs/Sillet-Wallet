// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;

namespace Sillet.Crypto
{
    public static class Hashes
    {
        public static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        public static byte[] Sha256d(byte[] data)
        {
            return Sha256(Sha256(data));
        }

        public static byte[] HmacSha512(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA512(key)) return hmac.ComputeHash(data);
        }

        public static byte[] Ripemd160(byte[] data)
        {
            var digest = new RipeMD160Digest();
            digest.BlockUpdate(data, 0, data.Length);
            var output = new byte[20];
            digest.DoFinal(output, 0);
            return output;
        }

        public static byte[] Hash160(byte[] data)
        {
            return Ripemd160(Sha256(data));
        }

        public static byte[] Blake2b(byte[] data, int bits)
        {
            var digest = new Blake2bDigest(bits);
            digest.BlockUpdate(data, 0, data.Length);
            var output = new byte[bits / 8];
            digest.DoFinal(output, 0);
            return output;
        }

        public static byte[] Keccak256(byte[] data)
        {
            var digest = new KeccakDigest(256);
            digest.BlockUpdate(data, 0, data.Length);
            var output = new byte[32];
            digest.DoFinal(output, 0);
            return output;
        }
    }
}
