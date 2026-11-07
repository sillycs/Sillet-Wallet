// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Ton
{
    public sealed class TonCell
    {
        public byte[] Data = new byte[0];
        public int BitLength;
        public List<TonCell> Refs = new List<TonCell>();
        private byte[] _hash;
        private int _depth = -1;

        public int Depth
        {
            get
            {
                if (_depth < 0) _depth = Refs.Count == 0 ? 0 : Refs.Max(r => r.Depth) + 1;
                return _depth;
            }
        }

        private byte[] Descriptors()
        {
            int d1 = Refs.Count;
            int d2 = BitLength / 8 + (BitLength + 7) / 8;
            return new[] { (byte)d1, (byte)d2 };
        }

        public byte[] PaddedData()
        {
            int bytes = (BitLength + 7) / 8;
            var result = new byte[bytes];
            Array.Copy(Data, result, Math.Min(bytes, Data.Length));
            if (BitLength % 8 != 0) result[bytes - 1] = (byte)((result[bytes - 1] & (0xFF << (8 - BitLength % 8))) | (1 << (7 - BitLength % 8)));
            return result;
        }

        public byte[] Hash()
        {
            if (_hash != null) return _hash;
            var parts = new List<byte[]> { Descriptors(), PaddedData() };
            foreach (var r in Refs) parts.Add(new[] { (byte)(r.Depth >> 8), (byte)r.Depth });
            foreach (var r in Refs) parts.Add(r.Hash());
            _hash = Hashes.Sha256(Bytes.Concat(parts.ToArray()));
            return _hash;
        }

        public static TonCell ParseBoc(byte[] boc)
        {
            if (boc.Length < 6 || boc[0] != 0xb5 || boc[1] != 0xee || boc[2] != 0x9c || boc[3] != 0x72) throw new FormatException("Not a BOC");
            int flags = boc[4];
            bool hasIdx = (flags & 0x80) != 0;
            bool hasCrc = (flags & 0x40) != 0;
            int sizeBytes = flags & 7;
            int offBytes = boc[5];
            int pos = 6;
            Func<int, long> read = n =>
            {
                long v = 0;
                for (int i = 0; i < n; i++) v = (v << 8) | boc[pos++];
                return v;
            };
            int cells = (int)read(sizeBytes);
            int roots = (int)read(sizeBytes);
            read(sizeBytes);
            read(offBytes);
            int rootIndex = (int)read(sizeBytes);
            if (hasIdx) pos += cells * offBytes;
            var raw = new List<Tuple<byte[], int, List<int>>>();
            for (int c = 0; c < cells; c++)
            {
                int d1 = boc[pos++];
                int d2 = boc[pos++];
                int refs = d1 & 7;
                int dataBytes = (d2 + 1) / 2;
                var data = new byte[dataBytes];
                Array.Copy(boc, pos, data, 0, dataBytes);
                pos += dataBytes;
                int bits = dataBytes * 8;
                if ((d2 & 1) != 0)
                {
                    int last = data[dataBytes - 1];
                    int tz = 0;
                    while (((last >> tz) & 1) == 0) tz++;
                    bits = dataBytes * 8 - tz - 1;
                }
                var list = new List<int>();
                for (int r = 0; r < refs; r++) list.Add((int)read(sizeBytes));
                raw.Add(Tuple.Create(data, bits, list));
            }
            var built = new TonCell[cells];
            for (int i = cells - 1; i >= 0; i--)
            {
                var cell = new TonCell { Data = raw[i].Item1, BitLength = raw[i].Item2 };
                foreach (var r in raw[i].Item3) cell.Refs.Add(built[r]);
                built[i] = cell;
            }
            return built[rootIndex];
        }

        public byte[] ToBoc()
        {
            var order = new List<TonCell>();
            var index = new Dictionary<string, int>();
            Action<TonCell> visit = null;
            var seen = new HashSet<string>();
            var topo = new List<TonCell>();
            visit = cell =>
            {
                var key = Hex.Encode(cell.Hash());
                if (seen.Contains(key)) return;
                seen.Add(key);
                foreach (var r in cell.Refs) visit(r);
                topo.Add(cell);
            };
            visit(this);
            topo.Reverse();
            var sorted = new List<TonCell>();
            var placed = new HashSet<string>();
            Action<TonCell> place = null;
            place = cell =>
            {
                var key = Hex.Encode(cell.Hash());
                if (placed.Contains(key)) return;
                placed.Add(key);
                sorted.Add(cell);
            };
            var queue = new List<TonCell> { this };
            var visited = new HashSet<string>();
            var ordered = new List<TonCell>();
            for (int i = 0; i < queue.Count; i++)
            {
                var key = Hex.Encode(queue[i].Hash());
                if (visited.Contains(key)) continue;
                visited.Add(key);
                ordered.Add(queue[i]);
                queue.AddRange(queue[i].Refs);
            }
            // keep every cell after all cells that reference it
            var final = new List<TonCell>();
            var done = new HashSet<string>();
            bool moved = true;
            var remaining = new List<TonCell>(ordered);
            while (remaining.Count > 0 && moved)
            {
                moved = false;
                foreach (var cell in remaining.ToList())
                {
                    var key = Hex.Encode(cell.Hash());
                    bool referenced = remaining.Any(o => !ReferenceEquals(o, cell) && o.Refs.Any(r => Hex.Encode(r.Hash()) == key));
                    if (referenced) continue;
                    final.Add(cell);
                    remaining.Remove(cell);
                    moved = true;
                    break;
                }
            }
            for (int i = 0; i < final.Count; i++) index[Hex.Encode(final[i].Hash())] = i;
            var body = new List<byte>();
            foreach (var cell in final)
            {
                body.AddRange(Descriptors2(cell));
                body.AddRange(cell.PaddedData());
                foreach (var r in cell.Refs) body.Add((byte)index[Hex.Encode(r.Hash())]);
            }
            if (final.Count > 255 || body.Count > 65535) throw new InvalidOperationException("Cell tree too large");
            var header = new List<byte> { 0xb5, 0xee, 0x9c, 0x72, 0x01, 0x02, (byte)final.Count, 0x01, 0x00, (byte)(body.Count >> 8), (byte)body.Count, 0x00 };
            return header.Concat(body).ToArray();
        }

        private static byte[] Descriptors2(TonCell cell)
        {
            return new[] { (byte)cell.Refs.Count, (byte)(cell.BitLength / 8 + (cell.BitLength + 7) / 8) };
        }
    }

    public sealed class TonBuilder
    {
        private readonly List<bool> _bits = new List<bool>();
        private readonly List<TonCell> _refs = new List<TonCell>();

        public TonBuilder Bit(bool value)
        {
            _bits.Add(value);
            return this;
        }

        public TonBuilder UInt(BigInteger value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--) _bits.Add(((value >> i) & 1) == 1);
            return this;
        }

        public TonBuilder Bytes(byte[] data)
        {
            foreach (var b in data) UInt(b, 8);
            return this;
        }

        public TonBuilder Coins(BigInteger value)
        {
            if (value.IsZero) return UInt(0, 4);
            var bytes = BigInts.ToUnsignedBigEndian(value);
            UInt(bytes.Length, 4);
            return Bytes(bytes);
        }

        public TonBuilder Ref(TonCell cell)
        {
            _refs.Add(cell);
            return this;
        }

        public int Length { get { return _bits.Count; } }

        public TonCell Build()
        {
            var cell = new TonCell { BitLength = _bits.Count, Data = new byte[(_bits.Count + 7) / 8] };
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) cell.Data[i / 8] |= (byte)(0x80 >> (i % 8));
            cell.Refs.AddRange(_refs);
            return cell;
        }
    }
}
