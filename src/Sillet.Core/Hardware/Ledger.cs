using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Chains.Evm;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Hardware
{
    public interface ILedgerTransport : IDisposable
    {
        Task<byte[]> ExchangeAsync(byte[] apdu, CancellationToken ct);
    }

    public sealed class LedgerAddress
    {
        public LedgerAddress(string address, byte[] publicKey)
        {
            Address = address;
            PublicKey = publicKey;
        }

        public string Address { get; }
        public byte[] PublicKey { get; }
    }

    public static class LedgerFraming
    {
        public const int PacketSize = 64;
        private const byte Tag = 0x05;

        public static List<byte[]> Pack(byte[] apdu)
        {
            var framed = new byte[apdu.Length + 2];
            framed[0] = (byte)(apdu.Length >> 8);
            framed[1] = (byte)apdu.Length;
            Buffer.BlockCopy(apdu, 0, framed, 2, apdu.Length);
            var packets = new List<byte[]>();
            int offset = 0;
            ushort sequence = 0;
            while (offset < framed.Length)
            {
                var packet = new byte[PacketSize];
                packet[0] = 0x01;
                packet[1] = 0x01;
                packet[2] = Tag;
                packet[3] = (byte)(sequence >> 8);
                packet[4] = (byte)sequence;
                var take = Math.Min(PacketSize - 5, framed.Length - offset);
                Buffer.BlockCopy(framed, offset, packet, 5, take);
                offset += take;
                sequence++;
                packets.Add(packet);
            }
            return packets;
        }

        public static List<byte[]> PackBle(byte[] apdu, int block)
        {
            var framed = new byte[apdu.Length + 2];
            framed[0] = (byte)(apdu.Length >> 8);
            framed[1] = (byte)apdu.Length;
            Buffer.BlockCopy(apdu, 0, framed, 2, apdu.Length);
            var packets = new List<byte[]>();
            int offset = 0;
            ushort sequence = 0;
            while (offset < framed.Length)
            {
                var take = Math.Min(block, framed.Length - offset);
                var packet = new byte[3 + take];
                packet[0] = Tag;
                packet[1] = (byte)(sequence >> 8);
                packet[2] = (byte)sequence;
                Buffer.BlockCopy(framed, offset, packet, 3, take);
                offset += take;
                sequence++;
                packets.Add(packet);
            }
            return packets;
        }

        public static async Task<byte[]> UnpackBleAsync(Func<Task<byte[]>> next)
        {
            byte[] data = null;
            int filled = 0, expected = 0;
            ushort sequence = 0;
            while (data == null || filled < expected)
            {
                var packet = await next();
                if (packet == null || packet.Length < 4 || packet[0] != Tag) throw new LedgerException("The Ledger sent an unexpected answer");
                var seq = (ushort)((packet[1] << 8) | packet[2]);
                if (seq != sequence) throw new LedgerException("The Ledger answer arrived out of order");
                int start = 3;
                if (sequence == 0)
                {
                    if (packet.Length < 5) throw new LedgerException("The Ledger sent an incomplete answer");
                    expected = (packet[3] << 8) | packet[4];
                    data = new byte[expected];
                    start = 5;
                }
                var take = Math.Min(packet.Length - start, expected - filled);
                Buffer.BlockCopy(packet, start, data, filled, take);
                filled += take;
                sequence++;
            }
            return data;
        }

        public static async Task<byte[]> UnpackAsync(Func<Task<byte[]>> next)
        {
            byte[] data = null;
            int filled = 0, expected = 0;
            ushort sequence = 0;
            while (data == null || filled < expected)
            {
                var packet = await next();
                if (packet == null || packet.Length < 6) throw new LedgerException("The Ledger sent an incomplete answer");
                if (packet[0] != 0x01 || packet[1] != 0x01 || packet[2] != Tag)
                    throw new LedgerException("The Ledger sent an unexpected answer");
                var seq = (ushort)((packet[3] << 8) | packet[4]);
                if (seq != sequence) throw new LedgerException("The Ledger answer arrived out of order");
                int start = 5;
                if (sequence == 0)
                {
                    expected = (packet[5] << 8) | packet[6];
                    data = new byte[expected];
                    start = 7;
                }
                var take = Math.Min(packet.Length - start, expected - filled);
                Buffer.BlockCopy(packet, start, data, filled, take);
                filled += take;
                sequence++;
            }
            return data;
        }
    }

    public sealed class LedgerException : Exception
    {
        public LedgerException(string message, int status = 0) : base(message)
        {
            Status = status;
        }

        public int Status { get; }

        public bool AppNotOpen
        {
            get { return Status == 0x6511 || Status == 0x6D00 || Status == 0x6E00 || Status == 0x6A87 || Status == 0x6E01 || Status == 0x6D02 || Status == 0x6700; }
        }
    }

    public static class LedgerProtocol
    {
        public static byte[] Apdu(byte cla, byte ins, byte p1, byte p2, byte[] data)
        {
            var apdu = new byte[5 + data.Length];
            apdu[0] = cla;
            apdu[1] = ins;
            apdu[2] = p1;
            apdu[3] = p2;
            apdu[4] = (byte)data.Length;
            Buffer.BlockCopy(data, 0, apdu, 5, data.Length);
            return apdu;
        }

        public static byte[] CheckStatus(byte[] answer, string app)
        {
            if (answer == null || answer.Length < 2) throw new LedgerException("The Ledger did not answer");
            var status = (answer[answer.Length - 2] << 8) | answer[answer.Length - 1];
            if (status != 0x9000)
            {
                switch (status)
                {
                    case 0x6985: throw new LedgerException("You rejected the transaction on your Ledger", status);
                    case 0x5515: case 0x6B0C: throw new LedgerException("Unlock your Ledger and try again", status);
                    case 0x6511: case 0x6D00: case 0x6E00: case 0x6A87: case 0x6E01: case 0x6D02: case 0x6700:
                        throw new LedgerException("Open the " + app + " app on your Ledger and try again", status);
                    case 0x6A80: case 0x6808:
                        throw new LedgerException("Turn on blind signing in the " + app + " app on your Ledger (Settings in the app)", status);
                    default: throw new LedgerException("The Ledger returned an error (0x" + status.ToString("X4") + ")", status);
                }
            }
            var data = new byte[answer.Length - 2];
            Buffer.BlockCopy(answer, 0, data, 0, data.Length);
            return data;
        }
    }

    public static class LedgerEthereum
    {
        public const string DefaultPath = "m/44'/60'/0'/0/0";
        private const byte Cla = 0xE0;
        private const int MaxChunk = 150;

        public static byte[] PathBytes(string path)
        {
            var parts = path.Split('/');
            var list = new List<uint>();
            foreach (var raw in parts)
            {
                if (raw == "m" || raw.Length == 0) continue;
                var hardened = raw.EndsWith("'", StringComparison.Ordinal);
                var number = uint.Parse(hardened ? raw.Substring(0, raw.Length - 1) : raw);
                list.Add(hardened ? number | 0x80000000u : number);
            }
            var bytes = new byte[1 + list.Count * 4];
            bytes[0] = (byte)list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                bytes[1 + i * 4] = (byte)(list[i] >> 24);
                bytes[2 + i * 4] = (byte)(list[i] >> 16);
                bytes[3 + i * 4] = (byte)(list[i] >> 8);
                bytes[4 + i * 4] = (byte)list[i];
            }
            return bytes;
        }

        private static byte[] Apdu(byte ins, byte p1, byte p2, byte[] data)
        {
            return LedgerProtocol.Apdu(Cla, ins, p1, p2, data);
        }

        public static byte[] CheckStatus(byte[] answer)
        {
            return LedgerProtocol.CheckStatus(answer, "Ethereum");
        }

        public static async Task<LedgerAddress> GetAddressAsync(ILedgerTransport transport, string path, CancellationToken ct)
        {
            var answer = CheckStatus(await transport.ExchangeAsync(Apdu(0x02, 0x00, 0x00, PathBytes(path)), ct));
            if (answer.Length < 2) throw new LedgerException("The Ledger returned an address that could not be read");
            int keyLength = answer[0];
            if (keyLength != 65 || answer.Length < 1 + keyLength + 1) throw new LedgerException("The Ledger returned an address that could not be read");
            var key = new byte[keyLength];
            Buffer.BlockCopy(answer, 1, key, 0, keyLength);
            return new LedgerAddress(EvmAddress.FromPublicKey(key), key);
        }

        public static async Task<EcdsaSignature> SignTransactionAsync(ILedgerTransport transport, string path, byte[] payload, long chainId, bool typed, CancellationToken ct)
        {
            var pathBytes = PathBytes(path);
            var all = new byte[pathBytes.Length + payload.Length];
            Buffer.BlockCopy(pathBytes, 0, all, 0, pathBytes.Length);
            Buffer.BlockCopy(payload, 0, all, pathBytes.Length, payload.Length);
            byte[] answer = null;
            int offset = 0;
            bool first = true;
            while (offset < all.Length)
            {
                var take = Math.Min(MaxChunk, all.Length - offset);
                var chunk = new byte[take];
                Buffer.BlockCopy(all, offset, chunk, 0, take);
                offset += take;
                answer = CheckStatus(await transport.ExchangeAsync(Apdu(0x04, (byte)(first ? 0x00 : 0x80), 0x00, chunk), ct));
                first = false;
            }
            if (answer == null || answer.Length < 65) throw new LedgerException("The Ledger returned a signature that could not be read");
            int v = answer[0];
            var r = new byte[32];
            var s = new byte[32];
            Buffer.BlockCopy(answer, 1, r, 0, 32);
            Buffer.BlockCopy(answer, 33, s, 0, 32);
            return new EcdsaSignature(r, s, RecoveryId(v, chainId, typed));
        }

        public static int RecoveryId(int v, long chainId, bool typed)
        {
            if (typed) return v & 1;
            if (v == 27 || v == 28) return v - 27;
            if (v >= 35) return (v - 35) & 1;
            return v & 1;
        }
    }

    public static class LedgerSolana
    {
        public const string DefaultPath = "m/44'/501'/0'/0'";
        public const string LedgerLivePath = "m/44'/501'/0'";
        private const byte Cla = 0xE0;
        private const int MaxPayload = 255;

        public static async Task<LedgerAddress> GetAddressAsync(ILedgerTransport transport, string path, CancellationToken ct)
        {
            var answer = LedgerProtocol.CheckStatus(await transport.ExchangeAsync(LedgerProtocol.Apdu(Cla, 0x05, 0x00, 0x00, LedgerEthereum.PathBytes(path)), ct), "Solana");
            if (answer.Length < 32) throw new LedgerException("The Ledger returned an address that could not be read");
            var key = new byte[32];
            Buffer.BlockCopy(answer, 0, key, 0, 32);
            return new LedgerAddress(Base58.Bitcoin.Encode(key), key);
        }

        public static async Task<byte[]> SignMessageAsync(ILedgerTransport transport, string path, byte[] message, CancellationToken ct)
        {
            var pathBytes = LedgerEthereum.PathBytes(path);
            var payload = new byte[1 + pathBytes.Length + message.Length];
            payload[0] = 1;
            Buffer.BlockCopy(pathBytes, 0, payload, 1, pathBytes.Length);
            Buffer.BlockCopy(message, 0, payload, 1 + pathBytes.Length, message.Length);
            byte[] answer = null;
            int offset = 0;
            byte p2 = 0x00;
            while (offset < payload.Length)
            {
                var take = Math.Min(MaxPayload, payload.Length - offset);
                var more = offset + take < payload.Length;
                var chunk = new byte[take];
                Buffer.BlockCopy(payload, offset, chunk, 0, take);
                offset += take;
                answer = LedgerProtocol.CheckStatus(await transport.ExchangeAsync(LedgerProtocol.Apdu(Cla, 0x06, 0x01, (byte)(p2 | (more ? 0x02 : 0x00)), chunk), ct), "Solana");
                p2 |= 0x01;
            }
            if (answer == null || answer.Length < 64) throw new LedgerException("The Ledger returned a signature that could not be read");
            var signature = new byte[64];
            Buffer.BlockCopy(answer, 0, signature, 0, 64);
            return signature;
        }
    }

    public static class LedgerTron
    {
        public const string DefaultPath = "m/44'/195'/0'/0/0";
        private const byte Cla = 0xE0;
        private const int ChunkSize = 250;

        public static async Task<LedgerAddress> GetAddressAsync(ILedgerTransport transport, string path, CancellationToken ct)
        {
            var answer = LedgerProtocol.CheckStatus(await transport.ExchangeAsync(LedgerProtocol.Apdu(Cla, 0x02, 0x00, 0x00, LedgerEthereum.PathBytes(path)), ct), "TRON");
            if (answer.Length < 2) throw new LedgerException("The Ledger returned an address that could not be read");
            int keyLength = answer[0];
            if (keyLength != 65 || answer.Length < 1 + keyLength + 1) throw new LedgerException("The Ledger returned an address that could not be read");
            var key = new byte[keyLength];
            Buffer.BlockCopy(answer, 1, key, 0, keyLength);
            int addressLength = answer[1 + keyLength];
            if (answer.Length < 2 + keyLength + addressLength) throw new LedgerException("The Ledger returned an address that could not be read");
            return new LedgerAddress(System.Text.Encoding.ASCII.GetString(answer, 2 + keyLength, addressLength), key);
        }

        public static List<byte[]> SplitFields(byte[] raw)
        {
            var fields = new List<byte[]>();
            int pos = 0;
            while (pos < raw.Length)
            {
                int start = pos;
                ReadVarint(raw, ref pos);
                var wire = raw[start] & 0x07;
                int end;
                switch (wire)
                {
                    case 0: ReadVarint(raw, ref pos); end = pos; break;
                    case 1: end = pos + 8; break;
                    case 2: { var length = (int)ReadVarint(raw, ref pos); end = pos + length; break; }
                    case 5: end = pos + 4; break;
                    default: throw new LedgerException("This TRON transaction has a format the Ledger cannot sign");
                }
                if (end > raw.Length) throw new LedgerException("This TRON transaction could not be read");
                var field = new byte[end - start];
                Buffer.BlockCopy(raw, start, field, 0, field.Length);
                fields.Add(field);
                pos = end;
            }
            return fields;
        }

        private static ulong ReadVarint(byte[] data, ref int pos)
        {
            ulong value = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= data.Length || shift > 63) throw new LedgerException("This TRON transaction could not be read");
                var b = data[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
        }

        public static async Task<byte[]> SignAsync(ILedgerTransport transport, string path, byte[] rawData, CancellationToken ct)
        {
            var chunks = new List<byte[]>();
            var current = new List<byte>(LedgerEthereum.PathBytes(path));
            foreach (var field in SplitFields(rawData))
            {
                if (field.Length + LedgerEthereum.PathBytes(path).Length > ChunkSize) throw new LedgerException("This TRON transaction is too large for the Ledger");
                if (current.Count + field.Length > ChunkSize)
                {
                    chunks.Add(current.ToArray());
                    current = new List<byte>();
                }
                current.AddRange(field);
            }
            chunks.Add(current.ToArray());
            byte[] answer = null;
            for (int i = 0; i < chunks.Count; i++)
            {
                byte p1;
                if (chunks.Count == 1) p1 = 0x10;
                else if (i == 0) p1 = 0x00;
                else if (i == chunks.Count - 1) p1 = 0x90;
                else p1 = 0x80;
                answer = LedgerProtocol.CheckStatus(await transport.ExchangeAsync(LedgerProtocol.Apdu(Cla, 0x04, p1, 0x00, chunks[i]), ct), "TRON");
            }
            if (answer == null || answer.Length < 65) throw new LedgerException("The Ledger returned a signature that could not be read");
            var signature = new byte[65];
            Buffer.BlockCopy(answer, 0, signature, 0, 65);
            if (signature[64] < 27) signature[64] = (byte)(signature[64] + 27);
            return signature;
        }
    }
}
