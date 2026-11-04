using System;

namespace Sillet.Encoding
{
    public static class Hex
    {
        private const string Digits = "0123456789abcdef";

        public static string Encode(byte[] data)
        {
            return Encode(data, 0, data.Length);
        }

        public static string Encode(byte[] data, int offset, int count)
        {
            var chars = new char[count * 2];
            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                chars[i * 2] = Digits[b >> 4];
                chars[i * 2 + 1] = Digits[b & 0xF];
            }
            return new string(chars);
        }

        public static byte[] Decode(string hex)
        {
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            if (hex.Length % 2 == 1) hex = "0" + hex;
            var result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = (byte)((Value(hex[i * 2]) << 4) | Value(hex[i * 2 + 1]));
            return result;
        }

        private static int Value(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new FormatException("Invalid hexadecimal text");
        }
    }
}
