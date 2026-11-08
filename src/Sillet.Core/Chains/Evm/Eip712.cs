// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Evm
{
    // Hashes typed data the way EIP-712 says, so a site can ask for a signature on structured data
    public sealed class Eip712
    {
        private sealed class Field
        {
            public string Name;
            public string Type;
        }

        private readonly Dictionary<string, List<Field>> _types = new Dictionary<string, List<Field>>();

        public string PrimaryType { get; private set; }
        public JsonValue Domain { get; private set; }
        public JsonValue Message { get; private set; }

        public static Eip712 Parse(string json)
        {
            JsonValue root;
            try { root = JsonValue.Parse(json); }
            catch (Exception) { throw new WalletException("The data to sign is not valid"); }
            if (!root.IsObject || !root.Has("types") || !root.Has("primaryType") || !root.Has("message")) throw new WalletException("The data to sign is not valid");
            var t = new Eip712 { PrimaryType = root["primaryType"].AsString(), Message = root["message"], Domain = root.Has("domain") ? root["domain"] : JsonValue.NewObject() };
            foreach (var type in root["types"].Properties)
            {
                var list = new List<Field>();
                foreach (var f in type.Value.Items) list.Add(new Field { Name = f["name"].AsString(), Type = f["type"].AsString() });
                t._types[type.Key] = list;
            }
            if (!t._types.ContainsKey("EIP712Domain")) throw new WalletException("The data to sign has no domain");
            if (!t._types.ContainsKey(t.PrimaryType)) throw new WalletException("The data to sign is not valid");
            return t;
        }

        public long? DomainChainId()
        {
            JsonValue v;
            if (!Domain.TryGet("chainId", out v) || v.IsNull) return null;
            return (long)ParseInt(v);
        }

        public string DomainName()
        {
            JsonValue v;
            return Domain.TryGet("name", out v) && v.IsString ? v.AsString() : null;
        }

        public string DomainContract()
        {
            JsonValue v;
            return Domain.TryGet("verifyingContract", out v) && v.IsString ? v.AsString() : null;
        }

        public byte[] Digest()
        {
            return Hashes.Keccak256(Bytes.Concat(new byte[] { 0x19, 0x01 }, HashStruct("EIP712Domain", Domain), HashStruct(PrimaryType, Message)));
        }

        private static readonly Regex ArrayType = new Regex(@"^(.*)\[(\d*)\]$", RegexOptions.Compiled);

        private static string BaseName(string type)
        {
            var m = ArrayType.Match(type);
            while (m.Success) { type = m.Groups[1].Value; m = ArrayType.Match(type); }
            return type;
        }

        private void Collect(string type, SortedSet<string> found)
        {
            type = BaseName(type);
            if (!_types.ContainsKey(type) || found.Contains(type)) return;
            found.Add(type);
            foreach (var f in _types[type]) Collect(f.Type, found);
        }

        private string EncodeType(string primary)
        {
            var deps = new SortedSet<string>(StringComparer.Ordinal);
            Collect(primary, deps);
            deps.Remove(primary);
            var order = new List<string> { primary };
            order.AddRange(deps);
            var sb = new StringBuilder();
            foreach (var name in order)
                sb.Append(name).Append('(').Append(string.Join(",", _types[name].Select(f => f.Type + " " + f.Name))).Append(')');
            return sb.ToString();
        }

        private byte[] HashStruct(string type, JsonValue data)
        {
            var parts = new List<byte[]> { Hashes.Keccak256(System.Text.Encoding.UTF8.GetBytes(EncodeType(type))) };
            foreach (var f in _types[type])
            {
                JsonValue v;
                if (!data.TryGet(f.Name, out v)) v = JsonValue.Null;
                parts.Add(EncodeValue(f.Type, v));
            }
            return Hashes.Keccak256(Bytes.Concat(parts.ToArray()));
        }

        private static BigInteger ParseInt(JsonValue v)
        {
            if (v.IsNumber) return BigInteger.Parse(v.ToJson(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (v.IsTrue) return BigInteger.One;
            var s = v.IsString ? v.AsString().Trim() : "0";
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return s.Length == 2 ? BigInteger.Zero : BigInteger.Parse("0" + s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (s.StartsWith("-0x", StringComparison.OrdinalIgnoreCase)) return -BigInteger.Parse("0" + s.Substring(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return s.Length == 0 ? BigInteger.Zero : BigInteger.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        private static byte[] Pad32Int(BigInteger value)
        {
            var raw = value.ToByteArray(); // little endian, two's complement
            var outp = new byte[32];
            var fill = value.Sign < 0 ? (byte)0xff : (byte)0;
            for (int i = 0; i < 32; i++) outp[i] = fill;
            for (int i = 0; i < raw.Length && i < 32; i++) outp[31 - i] = raw[i];
            return outp;
        }

        private static byte[] HexOrText(JsonValue v)
        {
            if (!v.IsString) throw new WalletException("The data to sign is not valid");
            var s = v.AsString();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Hex.Decode(s.Substring(2));
            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        private byte[] EncodeValue(string type, JsonValue v)
        {
            var arr = ArrayType.Match(type);
            if (arr.Success)
            {
                if (!v.IsArray) throw new WalletException("The data to sign is not valid");
                var inner = arr.Groups[1].Value;
                return Hashes.Keccak256(Bytes.Concat(v.Items.Select(i => EncodeValue(inner, i)).ToArray()));
            }
            if (_types.ContainsKey(type))
            {
                if (v.IsNull) return new byte[32];
                return HashStruct(type, v);
            }
            if (type == "string") return Hashes.Keccak256(System.Text.Encoding.UTF8.GetBytes(v.IsString ? v.AsString() : ""));
            if (type == "bytes") return Hashes.Keccak256(v.IsNull ? new byte[0] : HexOrText(v));
            if (type == "bool") return Pad32Int(v.IsTrue || (v.IsString && v.AsString() == "true") || (v.IsNumber && ParseInt(v) != 0) ? BigInteger.One : BigInteger.Zero);
            if (type == "address")
            {
                var a = v.IsString ? v.AsString() : "";
                // only the 20 bytes matter here, a site may not capitalize the address as a checksum
                if (!Regex.IsMatch(a, "^0x[0-9a-fA-F]{40}$")) throw new WalletException("The data to sign has an invalid address");
                return Bytes.Concat(new byte[12], Hex.Decode(a.Substring(2)));
            }
            var fixedBytes = Regex.Match(type, @"^bytes(\d+)$");
            if (fixedBytes.Success)
            {
                var n = int.Parse(fixedBytes.Groups[1].Value);
                if (n < 1 || n > 32) throw new WalletException("The data to sign is not valid");
                var raw = HexOrText(v);
                if (raw.Length > n) throw new WalletException("The data to sign is not valid");
                var outp = new byte[32];
                Array.Copy(raw, 0, outp, 0, raw.Length);
                return outp;
            }
            var integer = Regex.Match(type, @"^(u?)int(\d*)$");
            if (integer.Success)
            {
                var value = ParseInt(v);
                var bits = integer.Groups[2].Value.Length == 0 ? 256 : int.Parse(integer.Groups[2].Value);
                if (bits < 8 || bits > 256 || bits % 8 != 0) throw new WalletException("The data to sign is not valid");
                var unsigned = integer.Groups[1].Value == "u";
                var limit = BigInteger.One << (unsigned ? bits : bits - 1);
                if (unsigned ? (value < 0 || value >= limit) : (value < -limit || value >= limit)) throw new WalletException("The data to sign has a number out of range");
                return Pad32Int(value);
            }
            throw new WalletException("The data to sign uses a type Sillet does not know: " + type);
        }
    }
}
