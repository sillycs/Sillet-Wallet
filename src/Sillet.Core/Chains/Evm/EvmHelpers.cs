using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Evm
{
    public static class EvmAddress
    {
        private static readonly Regex Format = new Regex("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);

        public static string FromPublicKey(byte[] uncompressed65)
        {
            var hash = Hashes.Keccak256(Bytes.Skip(uncompressed65, 1));
            return ToChecksum(Hex.Encode(hash, 12, 20));
        }

        public static string ToChecksum(string address)
        {
            var lower = (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address.Substring(2) : address).ToLowerInvariant();
            var hash = Hex.Encode(Hashes.Keccak256(System.Text.Encoding.ASCII.GetBytes(lower)));
            var chars = lower.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsLetter(chars[i]) && hash[i] >= '8') chars[i] = char.ToUpperInvariant(chars[i]);
            return "0x" + new string(chars);
        }

        public static bool IsValid(string address)
        {
            if (address == null || !Format.IsMatch(address)) return false;
            var body = address.Substring(2);
            if (body == body.ToLowerInvariant() || body == body.ToUpperInvariant()) return true;
            return ToChecksum(address) == address;
        }

        public static byte[] ToBytes(string address)
        {
            return Hex.Decode(address);
        }
    }

    public static class EvmAbi
    {
        public static byte[] Selector(string signature)
        {
            return Encoding.Bytes.Take(Hashes.Keccak256(System.Text.Encoding.ASCII.GetBytes(signature)), 4);
        }

        public static byte[] Address(string address)
        {
            var word = new byte[32];
            var bytes = EvmAddress.ToBytes(address);
            Buffer.BlockCopy(bytes, 0, word, 12, 20);
            return word;
        }

        public static byte[] Uint(BigInteger value)
        {
            var bytes = BigInts.ToUnsignedBigEndian(value);
            if (bytes.Length > 32) throw new ArgumentOutOfRangeException(nameof(value));
            var word = new byte[32];
            Buffer.BlockCopy(bytes, 0, word, 32 - bytes.Length, bytes.Length);
            return word;
        }

        public static byte[] Bytes(byte[] data)
        {
            int padded = (data.Length + 31) / 32 * 32;
            var result = new byte[64 + padded];
            Buffer.BlockCopy(Uint(32), 0, result, 0, 32);
            Buffer.BlockCopy(Uint(data.Length), 0, result, 32, 32);
            Buffer.BlockCopy(data, 0, result, 64, data.Length);
            return result;
        }

        public static BigInteger DecodeUint(string hex)
        {
            return BigInts.ParseHex(hex);
        }

        public static string DecodeString(string hex)
        {
            var data = Hex.Decode(hex);
            string text;
            if (data.Length == 32)
                text = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
            else if (data.Length >= 96)
            {
                var offset = (int)BigInts.ParseHex("0x" + Hex.Encode(data, 0, 32));
                var length = (int)BigInts.ParseHex("0x" + Hex.Encode(data, offset, 32));
                if (offset < 0 || length < 0 || offset + 32 + length > data.Length) return "";
                text = System.Text.Encoding.UTF8.GetString(data, offset + 32, length);
            }
            else return "";
            var clean = new System.Text.StringBuilder();
            foreach (var c in text) if (c >= ' ' && c <= '~') clean.Append(c);
            return clean.ToString().Trim();
        }

        public static string Quantity(BigInteger value)
        {
            return BigInts.ToQuantity(value);
        }
    }

    public sealed class EvmTransaction
    {
        public EvmTransaction()
        {
            Data = Encoding.Bytes.Empty;
        }

        public long ChainId { get; set; }
        public BigInteger Nonce { get; set; }
        public BigInteger GasLimit { get; set; }
        public byte[] To { get; set; }
        public BigInteger Value { get; set; }
        public byte[] Data { get; set; }
        public BigInteger? MaxFeePerGas { get; set; }
        public BigInteger MaxPriorityFeePerGas { get; set; }
        public BigInteger GasPrice { get; set; }

        public bool IsEip1559
        {
            get { return MaxFeePerGas.HasValue; }
        }

        private List<byte[]> CommonFields()
        {
            if (IsEip1559)
            {
                return new List<byte[]>
                {
                    Rlp.EncodeInt(ChainId), Rlp.EncodeInt(Nonce), Rlp.EncodeInt(MaxPriorityFeePerGas), Rlp.EncodeInt(MaxFeePerGas.Value),
                    Rlp.EncodeInt(GasLimit), Rlp.EncodeBytes(To), Rlp.EncodeInt(Value), Rlp.EncodeBytes(Data), Rlp.EncodeList(),
                };
            }
            return new List<byte[]>
            {
                Rlp.EncodeInt(Nonce), Rlp.EncodeInt(GasPrice), Rlp.EncodeInt(GasLimit), Rlp.EncodeBytes(To),
                Rlp.EncodeInt(Value), Rlp.EncodeBytes(Data),
            };
        }

        public byte[] SigningPayload()
        {
            var fields = CommonFields();
            if (IsEip1559) return Encoding.Bytes.Concat(new byte[] { 0x02 }, Rlp.EncodeList(fields.ToArray()));
            fields.Add(Rlp.EncodeInt(ChainId));
            fields.Add(Rlp.EncodeInt(0));
            fields.Add(Rlp.EncodeInt(0));
            return Rlp.EncodeList(fields.ToArray());
        }

        public byte[] Sign(byte[] privateKey)
        {
            return WithSignature(Secp256k1.Sign(Hashes.Keccak256(SigningPayload()), privateKey));
        }

        public byte[] WithSignature(EcdsaSignature sig)
        {
            var r = Rlp.EncodeInt(BigInts.FromUnsignedBigEndian(sig.R));
            var s = Rlp.EncodeInt(BigInts.FromUnsignedBigEndian(sig.S));
            var fields = CommonFields();
            if (IsEip1559)
            {
                fields.Add(Rlp.EncodeInt(sig.RecoveryId));
                fields.Add(r);
                fields.Add(s);
                return Encoding.Bytes.Concat(new byte[] { 0x02 }, Rlp.EncodeList(fields.ToArray()));
            }
            var v = new BigInteger(sig.RecoveryId) + new BigInteger(ChainId) * 2 + 35;
            fields.Add(Rlp.EncodeInt(v));
            fields.Add(r);
            fields.Add(s);
            return Rlp.EncodeList(fields.ToArray());
        }
    }
}
