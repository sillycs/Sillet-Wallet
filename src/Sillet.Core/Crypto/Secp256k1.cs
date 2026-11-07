// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Sillet.Encoding;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace Sillet.Crypto
{
    public sealed class EcdsaSignature
    {
        public EcdsaSignature(byte[] r, byte[] s, int recoveryId)
        {
            R = r;
            S = s;
            RecoveryId = recoveryId;
        }

        public byte[] R { get; }
        public byte[] S { get; }
        public int RecoveryId { get; }

        public byte[] ToDer()
        {
            var r = DerInt(R);
            var s = DerInt(S);
            return Bytes.Concat(new[] { (byte)0x30, (byte)(r.Length + s.Length) }, r, s);
        }

        private static byte[] DerInt(byte[] value)
        {
            int i = 0;
            while (i < value.Length - 1 && value[i] == 0 && (value[i + 1] & 0x80) == 0) i++;
            var trimmed = Bytes.Skip(value, i);
            if ((trimmed[0] & 0x80) != 0) trimmed = Bytes.Concat(new byte[] { 0 }, trimmed);
            return Bytes.Concat(new[] { (byte)0x02, (byte)trimmed.Length }, trimmed);
        }
    }

    public static class Secp256k1
    {
        private static readonly X9ECParameters Params = CustomNamedCurves.GetByName("secp256k1");
        private static readonly ECDomainParameters Domain = new ECDomainParameters(Params.Curve, Params.G, Params.N, Params.H);

        public static BigInteger N
        {
            get { return Params.N; }
        }

        public static bool IsValidPrivateKey(byte[] key)
        {
            if (key.Length != 32) return false;
            var d = new BigInteger(1, key);
            return d.SignValue > 0 && d.CompareTo(N) < 0;
        }

        public static byte[] PublicKey(byte[] privateKey, bool compressed = true)
        {
            if (!IsValidPrivateKey(privateKey)) throw new CryptographicException("Invalid private key");
            return Params.G.Multiply(new BigInteger(1, privateKey)).Normalize().GetEncoded(compressed);
        }

        public static byte[] Decompress(byte[] publicKey)
        {
            return Params.Curve.DecodePoint(publicKey).Normalize().GetEncoded(false);
        }

        internal static byte[] AddPrivateKeys(byte[] a, byte[] b)
        {
            var sum = new BigInteger(1, a).Add(new BigInteger(1, b)).Mod(N);
            if (sum.SignValue == 0) throw new CryptographicException("Invalid derivation");
            return To32(sum);
        }

        public static EcdsaSignature Sign(byte[] hash32, byte[] privateKey)
        {
            if (hash32.Length != 32) throw new ArgumentException("The hash must be 32 bytes");
            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
            signer.Init(true, new ECPrivateKeyParameters(new BigInteger(1, privateKey), Domain));
            var rs = signer.GenerateSignature(hash32);
            var r = rs[0];
            var s = rs[1];
            if (s.CompareTo(N.ShiftRight(1)) > 0) s = N.Subtract(s);

            var expected = PublicKey(privateKey, false);
            for (int recId = 0; recId < 4; recId++)
            {
                var recovered = Recover(hash32, r, s, recId);
                if (recovered != null && Bytes.Equal(recovered, expected))
                    return new EcdsaSignature(To32(r), To32(s), recId);
            }
            throw new CryptographicException("Could not compute the recovery id");
        }

        public static byte[] Recover(byte[] hash32, BigInteger r, BigInteger s, int recId)
        {
            var n = N;
            var x = r.Add(BigInteger.ValueOf(recId / 2).Multiply(n));
            if (x.CompareTo(Params.Curve.Field.Characteristic) >= 0) return null;

            var encoded = new byte[33];
            encoded[0] = (byte)((recId & 1) == 1 ? 0x03 : 0x02);
            Buffer.BlockCopy(To32(x), 0, encoded, 1, 32);
            ECPoint point;
            try { point = Params.Curve.DecodePoint(encoded); }
            catch (ArgumentException) { return null; }
            if (!point.Multiply(n).IsInfinity) return null;

            var e = new BigInteger(1, hash32);
            var eNeg = BigInteger.Zero.Subtract(e).Mod(n);
            var rInv = r.ModInverse(n);
            var q = ECAlgorithms.SumOfTwoMultiplies(Params.G, rInv.Multiply(eNeg).Mod(n), point, rInv.Multiply(s).Mod(n)).Normalize();
            return q.IsInfinity ? null : q.GetEncoded(false);
        }

        internal static byte[] To32(BigInteger value)
        {
            var bytes = value.ToByteArrayUnsigned();
            if (bytes.Length > 32) throw new CryptographicException("Value out of range");
            var output = new byte[32];
            Buffer.BlockCopy(bytes, 0, output, 32 - bytes.Length, bytes.Length);
            return output;
        }
    }
}
