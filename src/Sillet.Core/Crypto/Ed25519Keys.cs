// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Sillet.Crypto
{
    public static class Ed25519Keys
    {
        public static byte[] PublicKey(byte[] privateSeed32)
        {
            var pk = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(privateSeed32, 0, pk, 0);
            return pk;
        }

        public static byte[] Sign(byte[] privateSeed32, byte[] message)
        {
            var sig = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(privateSeed32, 0, message, 0, message.Length, sig, 0);
            return sig;
        }

        public static bool Verify(byte[] publicKey32, byte[] message, byte[] signature64)
        {
            return Ed25519.Verify(signature64, 0, publicKey32, 0, message, 0, message.Length);
        }

        public static bool IsOnCurve(byte[] publicKey32)
        {
            return Ed25519.ValidatePublicKeyPartial(publicKey32, 0);
        }
    }
}
