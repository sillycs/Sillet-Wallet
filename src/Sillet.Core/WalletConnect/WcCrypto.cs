// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Security;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.WalletConnect
{
    // The cryptography WalletConnect v2 asks for: X25519 key agreement, HKDF, ChaCha20-Poly1305 and a signed token for the relay
    public static class WcCrypto
    {
        private static readonly SecureRandom Random = new SecureRandom();

        private static long _idCounter;

        // Message ids are the time in milliseconds followed by three digits, small enough for JavaScript apps to hold exactly
        public static long PayloadId()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L + (System.Threading.Interlocked.Increment(ref _idCounter) % 1000L);
        }

        public static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            Random.NextBytes(b);
            return b;
        }

        public static void NewKeyPair(out byte[] privateKey, out byte[] publicKey)
        {
            privateKey = new byte[32];
            X25519.GeneratePrivateKey(Random, privateKey);
            publicKey = new byte[32];
            X25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        }

        public static byte[] SharedKey(byte[] privateKey, byte[] peerPublicKey)
        {
            var secret = new byte[32];
            if (!X25519.CalculateAgreement(privateKey, 0, peerPublicKey, 0, secret, 0)) throw new InvalidOperationException("Bad key from the app");
            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(secret, null, null));
            var key = new byte[32];
            hkdf.GenerateBytes(key, 0, 32);
            return key;
        }

        public static string TopicOf(byte[] key)
        {
            return Hex.Encode(Hashes.Sha256(key));
        }

        // type 0 envelope: 0x00 | 12 byte nonce | ciphertext + tag, as base64
        public static string Encrypt(byte[] key, byte[] plain)
        {
            var iv = RandomBytes(12);
            var cipher = new ChaCha20Poly1305();
            cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, iv, null));
            var sealedBytes = new byte[cipher.GetOutputSize(plain.Length)];
            var len = cipher.ProcessBytes(plain, 0, plain.Length, sealedBytes, 0);
            cipher.DoFinal(sealedBytes, len);
            var env = new byte[1 + 12 + sealedBytes.Length];
            env[0] = 0;
            Buffer.BlockCopy(iv, 0, env, 1, 12);
            Buffer.BlockCopy(sealedBytes, 0, env, 13, sealedBytes.Length);
            return Convert.ToBase64String(env);
        }

        public static byte[] Decrypt(byte[] key, string base64)
        {
            var env = Convert.FromBase64String(base64);
            if (env.Length < 1 + 12 + 16) throw new InvalidOperationException("Message too short");
            int ivAt;
            if (env[0] == 0) ivAt = 1;
            else if (env[0] == 1) ivAt = 33; // type 1 also carries the sender public key
            else throw new InvalidOperationException("Unknown message type");
            if (env.Length < ivAt + 12 + 16) throw new InvalidOperationException("Message too short");
            var iv = new byte[12];
            Buffer.BlockCopy(env, ivAt, iv, 0, 12);
            var sealedLen = env.Length - ivAt - 12;
            var cipher = new ChaCha20Poly1305();
            cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, iv, null));
            var plain = new byte[cipher.GetOutputSize(sealedLen)];
            var len = cipher.ProcessBytes(env, ivAt + 12, sealedLen, plain, 0);
            len += cipher.DoFinal(plain, len);
            if (len != plain.Length) Array.Resize(ref plain, len);
            return plain;
        }

        private static string B64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // The relay wants a short lived token signed by a key of ours, so it knows the same client is coming back
        public static string RelayToken(byte[] ed25519Seed, string audience)
        {
            var pub = Ed25519Keys.PublicKey(ed25519Seed);
            var did = "did:key:z" + Base58.Bitcoin.Encode(Bytes.Concat(new byte[] { 0xed, 0x01 }, pub));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64Url(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"EdDSA\",\"typ\":\"JWT\"}"));
            var payload = B64Url(System.Text.Encoding.UTF8.GetBytes(Json.Serialize(new
            {
                iss = did,
                sub = Hex.Encode(RandomBytes(32)),
                aud = audience,
                iat = now,
                exp = now + 86400,
            })));
            var body = header + "." + payload;
            return body + "." + B64Url(Ed25519Keys.Sign(ed25519Seed, System.Text.Encoding.ASCII.GetBytes(body)));
        }
    }
}
