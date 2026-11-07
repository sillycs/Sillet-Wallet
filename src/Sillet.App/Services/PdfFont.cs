// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Sillet.App.Services
{
    internal sealed class PdfFont
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, int[]> _tables = new Dictionary<string, int[]>();
        private readonly Dictionary<int, int> _cmap = new Dictionary<int, int>();
        private int[] _advance;
        private int[] _loca;
        private bool _longLoca;
        private int _numGlyphs;
        private int _unitsPerEm;
        private short _ascent, _descent, _xMin, _yMin, _xMax, _yMax;

        public string Name { get; private set; }
        public string Path { get; private set; }
        public readonly SortedSet<int> Used = new SortedSet<int>();
        private readonly Dictionary<int, char> _chars = new Dictionary<int, char>();

        private PdfFont(byte[] data)
        {
            _data = data;
        }

        public static PdfFont[] FindArial()
        {
            var dirs = new List<string>();
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)); } catch (Exception) { }
            dirs.Add("/usr/share/fonts/truetype/liberation");
            dirs.Add("/usr/share/fonts/truetype/liberation2");
            var pairs = new[]
            {
                new[] { "arial.ttf", "arialbd.ttf" },
                new[] { "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf" }
            };
            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (var pair in pairs)
                {
                    var regular = Load(System.IO.Path.Combine(dir, pair[0]), "Arial");
                    var bold = Load(System.IO.Path.Combine(dir, pair[1]), "Arial-Bold");
                    if (regular != null && bold != null) return new[] { regular, bold };
                }
            }
            return null;
        }

        public static PdfFont Load(string path, string name)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var font = new PdfFont(File.ReadAllBytes(path)) { Name = name, Path = path };
                return font.Parse() ? font : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is IndexOutOfRangeException || e is ArgumentException)
            {
                return null;
            }
        }

        private int U16(int o) { return (_data[o] << 8) | _data[o + 1]; }
        private int S16(int o) { return (short)U16(o); }
        private long U32(int o) { return ((long)_data[o] << 24) | ((long)_data[o + 1] << 16) | ((long)_data[o + 2] << 8) | _data[o + 3]; }

        private bool Parse()
        {
            if (_data.Length < 12) return false;
            var tag = U32(0);
            if (tag != 0x00010000 && tag != 0x74727565) return false;
            int count = U16(4);
            for (int i = 0; i < count; i++)
            {
                int o = 12 + i * 16;
                var name = System.Text.Encoding.ASCII.GetString(_data, o, 4);
                _tables[name] = new[] { (int)U32(o + 8), (int)U32(o + 12) };
            }
            foreach (var need in new[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cmap" })
                if (!_tables.ContainsKey(need)) return false;

            int head = _tables["head"][0];
            _unitsPerEm = U16(head + 18);
            _xMin = (short)S16(head + 36); _yMin = (short)S16(head + 38); _xMax = (short)S16(head + 40); _yMax = (short)S16(head + 42);
            _longLoca = S16(head + 50) != 0;
            int hhea = _tables["hhea"][0];
            _ascent = (short)S16(hhea + 4);
            _descent = (short)S16(hhea + 6);
            int metrics = U16(hhea + 34);
            _numGlyphs = U16(_tables["maxp"][0] + 4);
            if (_unitsPerEm == 0 || _numGlyphs == 0 || metrics == 0) return false;

            _advance = new int[_numGlyphs];
            int hmtx = _tables["hmtx"][0];
            for (int g = 0; g < _numGlyphs; g++)
                _advance[g] = U16(hmtx + (g < metrics ? g : metrics - 1) * 4);

            _loca = new int[_numGlyphs + 1];
            int loca = _tables["loca"][0];
            for (int g = 0; g <= _numGlyphs; g++)
                _loca[g] = _longLoca ? (int)U32(loca + g * 4) : U16(loca + g * 2) * 2;

            return ParseCmap();
        }

        private bool ParseCmap()
        {
            int cmap = _tables["cmap"][0];
            int n = U16(cmap + 2);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                int platform = U16(cmap + 4 + i * 8);
                int encoding = U16(cmap + 6 + i * 8);
                int offset = (int)U32(cmap + 8 + i * 8);
                if ((platform == 3 && encoding == 1) || (platform == 0 && best < 0)) best = cmap + offset;
            }
            if (best < 0 || U16(best) != 4) return false;
            int segX2 = U16(best + 6);
            int ends = best + 14;
            int starts = ends + segX2 + 2;
            int deltas = starts + segX2;
            int ranges = deltas + segX2;
            for (int s = 0; s < segX2 / 2; s++)
            {
                int end = U16(ends + s * 2), start = U16(starts + s * 2), delta = S16(deltas + s * 2), ro = U16(ranges + s * 2);
                if (start == 0xFFFF) continue;
                for (int c = start; c <= end; c++)
                {
                    int g;
                    if (ro == 0) g = (c + delta) & 0xFFFF;
                    else
                    {
                        int at = ranges + s * 2 + ro + (c - start) * 2;
                        if (at + 1 >= _data.Length) continue;
                        g = U16(at);
                        if (g != 0) g = (g + delta) & 0xFFFF;
                    }
                    if (g != 0 && g < _numGlyphs) _cmap[c] = g;
                }
            }
            return _cmap.Count > 0;
        }

        public bool Has(char c)
        {
            return c < 32 || _cmap.ContainsKey(c);
        }

        public int Glyph(char c)
        {
            int g;
            return _cmap.TryGetValue(c, out g) ? g : 0;
        }

        public double Width(char c)
        {
            return _advance[Glyph(c)] * 1000.0 / _unitsPerEm;
        }

        public string Hex(string text)
        {
            var sb = new StringBuilder("<");
            foreach (var c in text ?? "")
            {
                var ch = c < 32 ? ' ' : c;
                var g = Glyph(ch);
                Used.Add(g);
                if (g != 0) _chars[g] = ch;
                sb.Append(g.ToString("X4"));
            }
            return sb.Append('>').ToString();
        }

        public List<byte[]> Objects(int first)
        {
            var latin = System.Text.Encoding.GetEncoding("iso-8859-1");
            var subset = Subset();
            var packed = Zlib(subset);
            var w = new StringBuilder("[");
            Used.Add(0);
            foreach (var g in Used) w.Append(g).Append(" [").Append(Math.Round(_advance[g] * 1000.0 / _unitsPerEm).ToString("0")).Append("] ");
            w.Append("]");
            double k = 1000.0 / _unitsPerEm;
            var list = new List<byte[]>();
            list.Add(latin.GetBytes("<< /Type /Font /Subtype /Type0 /BaseFont /" + Name + " /Encoding /Identity-H /DescendantFonts [" + (first + 1) + " 0 R] /ToUnicode " + (first + 4) + " 0 R >>"));
            list.Add(latin.GetBytes("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /" + Name + " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor " + (first + 2) + " 0 R /CIDToGIDMap /Identity /DW 1000 /W " + w + " >>"));
            list.Add(latin.GetBytes("<< /Type /FontDescriptor /FontName /" + Name + " /Flags 32 /FontBBox [" + (int)(_xMin * k) + " " + (int)(_yMin * k) + " " + (int)(_xMax * k) + " " + (int)(_yMax * k) + "] /ItalicAngle 0 /Ascent " + (int)(_ascent * k) + " /Descent " + (int)(_descent * k) + " /CapHeight " + (int)(_ascent * k * 0.8) + " /StemV 80 /FontFile2 " + (first + 3) + " 0 R >>"));
            var stream = new MemoryStream();
            var head = latin.GetBytes("<< /Length " + packed.Length + " /Length1 " + subset.Length + " /Filter /FlateDecode >>\nstream\n");
            stream.Write(head, 0, head.Length);
            stream.Write(packed, 0, packed.Length);
            var tail = latin.GetBytes("\nendstream");
            stream.Write(tail, 0, tail.Length);
            list.Add(stream.ToArray());
            var cmap = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
            var entries = new List<KeyValuePair<int, char>>(_chars);
            for (int i = 0; i < entries.Count; i += 100)
            {
                int n = Math.Min(100, entries.Count - i);
                cmap.Append(n).Append(" beginbfchar\n");
                for (int j = 0; j < n; j++) cmap.Append('<').Append(entries[i + j].Key.ToString("X4")).Append("> <").Append(((int)entries[i + j].Value).ToString("X4")).Append(">\n");
                cmap.Append("endbfchar\n");
            }
            cmap.Append("endcmap CMapName currentdict /CMap defineresource pop end end");
            var cmapBytes = latin.GetBytes(cmap.ToString());
            list.Add(latin.GetBytes("<< /Length " + cmapBytes.Length + " >>\nstream\n" + cmap + "\nendstream"));
            return list;
        }

        private byte[] Subset()
        {
            var keep = new SortedSet<int>(Used) { 0 };
            var queue = new Queue<int>(keep);
            int glyf = _tables["glyf"][0];
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                int start = _loca[g], end = _loca[g + 1];
                if (end - start < 10 || S16(glyf + start) >= 0) continue;
                int p = glyf + start + 10;
                while (true)
                {
                    int flags = U16(p), component = U16(p + 2);
                    if (component < _numGlyphs && keep.Add(component)) queue.Enqueue(component);
                    p += 4 + ((flags & 1) != 0 ? 4 : 2);
                    if ((flags & 0x8) != 0) p += 2; else if ((flags & 0x40) != 0) p += 4; else if ((flags & 0x80) != 0) p += 8;
                    if ((flags & 0x20) == 0) break;
                }
            }

            var newGlyf = new MemoryStream();
            var newLoca = new byte[(_numGlyphs + 1) * 4];
            for (int g = 0; g <= _numGlyphs; g++)
            {
                Put32(newLoca, g * 4, (int)newGlyf.Length);
                if (g < _numGlyphs && keep.Contains(g))
                {
                    int len = _loca[g + 1] - _loca[g];
                    if (len > 0) newGlyf.Write(_data, glyf + _loca[g], len);
                    while (newGlyf.Length % 4 != 0) newGlyf.WriteByte(0);
                }
            }
            Put32(newLoca, _numGlyphs * 4, (int)newGlyf.Length);

            var parts = new List<KeyValuePair<string, byte[]>>();
            foreach (var name in new[] { "cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "prep" })
            {
                int[] t;
                if (!_tables.TryGetValue(name, out t) && name != "glyf" && name != "loca") continue;
                byte[] body;
                if (name == "glyf") body = newGlyf.ToArray();
                else if (name == "loca") body = newLoca;
                else
                {
                    body = new byte[t[1]];
                    Buffer.BlockCopy(_data, t[0], body, 0, t[1]);
                    if (name == "head") { body[50] = 0; body[51] = 1; body[8] = body[9] = body[10] = body[11] = 0; }
                }
                parts.Add(new KeyValuePair<string, byte[]>(name, body));
            }

            var output = new MemoryStream();
            var header = new byte[12 + parts.Count * 16];
            Put32(header, 0, 0x00010000);
            header[4] = (byte)(parts.Count >> 8); header[5] = (byte)parts.Count;
            int pow = 1, log = 0;
            while (pow * 2 <= parts.Count) { pow *= 2; log++; }
            int searchRange = pow * 16;
            header[6] = (byte)(searchRange >> 8); header[7] = (byte)searchRange;
            header[8] = (byte)(log >> 8); header[9] = (byte)log;
            int rangeShift = parts.Count * 16 - searchRange;
            header[10] = (byte)(rangeShift >> 8); header[11] = (byte)rangeShift;
            int offset = header.Length;
            for (int i = 0; i < parts.Count; i++)
            {
                int at = 12 + i * 16;
                var tag = System.Text.Encoding.ASCII.GetBytes(parts[i].Key);
                Buffer.BlockCopy(tag, 0, header, at, 4);
                Put32(header, at + 4, (int)Checksum(parts[i].Value));
                Put32(header, at + 8, offset);
                Put32(header, at + 12, parts[i].Value.Length);
                offset += (parts[i].Value.Length + 3) & ~3;
            }
            output.Write(header, 0, header.Length);
            foreach (var part in parts)
            {
                output.Write(part.Value, 0, part.Value.Length);
                for (int pad = part.Value.Length; pad % 4 != 0; pad++) output.WriteByte(0);
            }
            return output.ToArray();
        }

        private static void Put32(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static uint Checksum(byte[] b)
        {
            uint sum = 0;
            for (int i = 0; i < b.Length; i += 4)
            {
                uint word = 0;
                for (int j = 0; j < 4; j++) word = (word << 8) | (i + j < b.Length ? b[i + j] : (byte)0);
                sum += word;
            }
            return sum;
        }

        private static byte[] Zlib(byte[] data)
        {
            var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            var adler = (b << 16) | a;
            output.WriteByte((byte)(adler >> 24)); output.WriteByte((byte)(adler >> 16)); output.WriteByte((byte)(adler >> 8)); output.WriteByte((byte)adler);
            return output.ToArray();
        }
    }
}
