// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Numerics;

namespace Sillet.Encoding
{
    public static class Rlp
    {
        public static byte[] EncodeBytes(byte[] value)
        {
            if (value.Length == 1 && value[0] < 0x80) return value;
            return Bytes.Concat(Prefix(value.Length, 0x80), value);
        }

        public static byte[] EncodeInt(BigInteger value)
        {
            if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
            return EncodeBytes(BigInts.ToUnsignedBigEndian(value));
        }

        public static byte[] EncodeList(params byte[][] encodedItems)
        {
            var body = Bytes.Concat(encodedItems);
            return Bytes.Concat(Prefix(body.Length, 0xc0), body);
        }

        private static byte[] Prefix(int length, byte offset)
        {
            if (length < 56) return new[] { (byte)(offset + length) };
            var len = BigInts.ToUnsignedBigEndian(new BigInteger(length));
            return Bytes.Concat(new[] { (byte)(offset + 55 + len.Length) }, len);
        }
    }
}
