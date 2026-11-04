using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sillet.Encoding
{
    public sealed class SegwitAddress
    {
        public SegwitAddress(int version, byte[] program)
        {
            Version = version;
            Program = program;
        }

        public int Version { get; }
        public byte[] Program { get; }
    }

    public static class Bech32
    {
        private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        private const uint Bech32Const = 1;
        private const uint Bech32mConst = 0x2bc830a3;
        private static readonly uint[] Generator = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };

        public static string EncodeSegwit(string hrp, int witnessVersion, byte[] program)
        {
            var data = new List<byte> { (byte)witnessVersion };
            data.AddRange(ConvertBits(program, 8, 5, true));
            return Encode(hrp, data.ToArray(), witnessVersion == 0 ? Bech32Const : Bech32mConst);
        }

        public static SegwitAddress DecodeSegwit(string hrp, string address)
        {
            string gotHrp;
            byte[] data;
            uint constant;
            if (!Decode(address, out gotHrp, out data, out constant)) return null;
            if (gotHrp != hrp || data.Length == 0) return null;
            int version = data[0];
            if (version > 16) return null;
            if (version == 0 && constant != Bech32Const) return null;
            if (version != 0 && constant != Bech32mConst) return null;
            var program = ConvertBits(Bytes.Skip(data, 1), 5, 8, false);
            if (program == null || program.Length < 2 || program.Length > 40) return null;
            if (version == 0 && program.Length != 20 && program.Length != 32) return null;
            return new SegwitAddress(version, program);
        }

        private static string Encode(string hrp, byte[] data, uint constant)
        {
            var checksum = CreateChecksum(hrp, data, constant);
            var sb = new StringBuilder(hrp + "1");
            foreach (var b in data.Concat(checksum)) sb.Append(Charset[b]);
            return sb.ToString();
        }

        private static bool Decode(string text, out string hrp, out byte[] data, out uint constant)
        {
            hrp = null;
            data = null;
            constant = 0;
            if (text.Length > 90) return false;
            bool hasLower = text.Any(char.IsLower), hasUpper = text.Any(char.IsUpper);
            if (hasLower && hasUpper) return false;
            text = text.ToLowerInvariant();
            int pos = text.LastIndexOf('1');
            if (pos < 1 || pos + 7 > text.Length) return false;
            var prefix = text.Substring(0, pos);
            if (prefix.Any(c => c < 33 || c > 126)) return false;
            var values = new byte[text.Length - pos - 1];
            for (int i = 0; i < values.Length; i++)
            {
                int v = Charset.IndexOf(text[pos + 1 + i]);
                if (v < 0) return false;
                values[i] = (byte)v;
            }
            uint polymod = Polymod(HrpExpand(prefix).Concat(values).ToArray());
            if (polymod != Bech32Const && polymod != Bech32mConst) return false;
            hrp = prefix;
            data = Bytes.Take(values, values.Length - 6);
            constant = polymod;
            return true;
        }

        private static uint Polymod(byte[] values)
        {
            uint chk = 1;
            foreach (var v in values)
            {
                uint top = chk >> 25;
                chk = ((chk & 0x1ffffff) << 5) ^ v;
                for (int i = 0; i < 5; i++)
                    if (((top >> i) & 1) != 0) chk ^= Generator[i];
            }
            return chk;
        }

        private static byte[] HrpExpand(string hrp)
        {
            var result = new List<byte>();
            foreach (var c in hrp) result.Add((byte)(c >> 5));
            result.Add(0);
            foreach (var c in hrp) result.Add((byte)(c & 31));
            return result.ToArray();
        }

        private static byte[] CreateChecksum(string hrp, byte[] data, uint constant)
        {
            var values = HrpExpand(hrp).Concat(data).Concat(new byte[6]).ToArray();
            uint mod = Polymod(values) ^ constant;
            var result = new byte[6];
            for (int i = 0; i < 6; i++) result[i] = (byte)((mod >> (5 * (5 - i))) & 31);
            return result;
        }

        private static byte[] ConvertBits(byte[] data, int from, int to, bool pad)
        {
            int acc = 0, bits = 0, maxv = (1 << to) - 1;
            var result = new List<byte>();
            foreach (var value in data)
            {
                if ((value >> from) != 0) return null;
                acc = (acc << from) | value;
                bits += from;
                while (bits >= to)
                {
                    bits -= to;
                    result.Add((byte)((acc >> bits) & maxv));
                }
            }
            if (pad)
            {
                if (bits > 0) result.Add((byte)((acc << (to - bits)) & maxv));
            }
            else if (bits >= from || ((acc << (to - bits)) & maxv) != 0)
            {
                return null;
            }
            return result.ToArray();
        }
    }
}
