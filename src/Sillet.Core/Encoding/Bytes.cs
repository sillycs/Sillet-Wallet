// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Globalization;
using System.Numerics;

namespace Sillet.Encoding
{
    public static class Bytes
    {
        public static readonly byte[] Empty = new byte[0];

        public static byte[] Concat(params byte[][] parts)
        {
            int length = 0;
            foreach (var part in parts) length += part.Length;
            var result = new byte[length];
            int offset = 0;
            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }

        public static byte[] Slice(byte[] data, int start, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(data, start, result, 0, length);
            return result;
        }

        public static byte[] Take(byte[] data, int count)
        {
            return Slice(data, 0, count);
        }

        public static byte[] Skip(byte[] data, int start)
        {
            return Slice(data, start, data.Length - start);
        }

        public static byte[] TakeLast(byte[] data, int count)
        {
            return Slice(data, data.Length - count, count);
        }

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        public static byte[] UInt32BE(uint value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        public static byte[] UInt32LE(uint value)
        {
            return new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };
        }

        public static byte[] UInt16LE(ushort value)
        {
            return new[] { (byte)value, (byte)(value >> 8) };
        }

        public static byte[] UInt64LE(ulong value)
        {
            var result = new byte[8];
            for (int i = 0; i < 8; i++) result[i] = (byte)(value >> (8 * i));
            return result;
        }
    }

    public static class BigInts
    {
        public static byte[] ToUnsignedBigEndian(BigInteger value)
        {
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value.IsZero) return Bytes.Empty;
            var little = value.ToByteArray();
            int length = little.Length;
            while (length > 1 && little[length - 1] == 0) length--;
            var result = new byte[length];
            for (int i = 0; i < length; i++) result[i] = little[length - 1 - i];
            return result;
        }

        public static BigInteger FromUnsignedBigEndian(byte[] bigEndian)
        {
            var little = new byte[bigEndian.Length + 1];
            for (int i = 0; i < bigEndian.Length; i++) little[i] = bigEndian[bigEndian.Length - 1 - i];
            return new BigInteger(little);
        }

        public static BigInteger ParseHex(string hex)
        {
            var body = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex;
            if (body.Length == 0) return BigInteger.Zero;
            return BigInteger.Parse("0" + body, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public static string ToQuantity(BigInteger value)
        {
            if (value.IsZero) return "0x0";
            return "0x" + value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        }
    }
}
