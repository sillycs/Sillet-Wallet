using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sillet.App.Services
{
    /// <summary>What a printed receipt says. Plain strings, already translated and formatted.</summary>
    public sealed class ReceiptDocument
    {
        public string Brand = "Sillet";
        public string Heading = "";      // "Payment receipt"
        public string Status = "";       // "Completed"
        public string Kind = "";         // "Sent" / "Received" / "Swap"
        public string Amount = "";       // "-0.5 ETH"
        public string Fiat = "";         // "about $1,200.00"
        public bool Incoming;
        public string Footer = "";
        public string Generated = "";
        public readonly List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
        public readonly List<string> Mono = new List<string>(); // labels whose value is an address / id
    }

    /// <summary>A small, dependency free PDF writer: one A4 page, Arial embedded when the system has it (Helvetica otherwise), laid out like a bank receipt.</summary>
    public static class ReceiptPdf
    {
        private const double W = 595, H = 842, Margin = 48;

        private static readonly short[] Widths =
        {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
            556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
            667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
            556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
        };

        private static PdfFont _regular, _bold;
        private static readonly object Gate = new object();
        private static bool _fontsLooked;
        private static PdfFont[] _fonts;

        /// <summary>Test hook: use these font files (regular, bold) instead of looking for Arial.</summary>
        public static string[] FontFiles;

        private static PdfFont[] LoadFonts()
        {
            if (FontFiles != null && FontFiles.Length == 2)
            {
                var r = PdfFont.Load(FontFiles[0], "Arial");
                var b = PdfFont.Load(FontFiles[1], "Arial-Bold");
                return r != null && b != null ? new[] { r, b } : null;
            }
            if (!_fontsLooked)
            {
                _fontsLooked = true;
                _fonts = PdfFont.FindArial();
            }
            if (_fonts == null) return null;
            // Fresh copies per receipt, so the used glyph lists start empty
            return new[] { PdfFont.Load(_fonts[0].Path, "Arial"), PdfFont.Load(_fonts[1].Path, "Arial-Bold") };
        }

        private static double Measure(string text, double size, bool bold, bool mono = false)
        {
            if (mono) return (text ?? "").Length * 0.6 * size;
            var face = bold ? _bold : _regular;
            if (face != null)
            {
                double sum = 0;
                foreach (var ch in text ?? "") sum += face.Width(ch);
                return sum * size / 1000.0;
            }
            double total = 0;
            foreach (var c in text)
            {
                int i = c - 32;
                total += i >= 0 && i < Widths.Length ? Widths[i] : 556;
            }
            return total * size / 1000.0 * (bold ? 1.07 : 1.0);
        }

        private static string Pdf(string text)
        {
            var sb = new StringBuilder();
            foreach (var raw in (text ?? "").Replace("œ", "oe").Replace("Œ", "OE"))
            {
                var c = raw;
                switch (c)
                {
                    case '–': case '—': c = '-'; break;
                    case '‘': case '’': c = '\''; break;
                    case '“': case '”': c = '"'; break;
                    case '•': c = '·'; break;
                    case '€': c = 'E'; break;
                }
                if (c == '\\' || c == '(' || c == ')') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else if (c < 127) sb.Append(c);
                else if (c <= 255) sb.Append("\\").Append(Convert.ToString((int)c, 8).PadLeft(3, '0'));
                else sb.Append('?');
            }
            return sb.ToString();
        }

        private static string N(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private sealed class Canvas
        {
            public readonly StringBuilder Ops = new StringBuilder();

            public void Rect(double x, double top, double w, double h, double r, double g, double b)
            {
                Ops.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" rg ")
                   .Append(N(x)).Append(' ').Append(N(H - top - h)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re f\n");
            }

            public void Line(double x1, double x2, double top, double r, double g, double b, double width = 0.6)
            {
                Ops.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" RG ").Append(N(width)).Append(" w ")
                   .Append(N(x1)).Append(' ').Append(N(H - top)).Append(" m ").Append(N(x2)).Append(' ').Append(N(H - top)).Append(" l S\n");
            }

            public void Text(string text, double x, double top, double size, string font, double r, double g, double b)
            {
                var face = font == "F1" ? _regular : font == "F2" ? _bold : null;
                Ops.Append("BT /").Append(font).Append(' ').Append(N(size)).Append(" Tf ")
                   .Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" rg ")
                   .Append(N(x)).Append(' ').Append(N(H - top)).Append(" Td ");
                if (face != null) Ops.Append(face.Hex(text)).Append(" Tj ET\n");
                else Ops.Append('(').Append(Pdf(text)).Append(") Tj ET\n");
            }
        }

        private static List<string> Wrap(string text, double size, bool bold, double width, bool mono = false)
        {
            var lines = new List<string>();
            text = text ?? "";
            var line = new StringBuilder();
            foreach (var word in text.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, size, bold, mono) <= width) { line.Clear(); line.Append(candidate); continue; }
                if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                var rest = word;
                while (Measure(rest, size, bold, mono) > width && rest.Length > 1)
                {
                    int cut = rest.Length - 1;
                    while (cut > 1 && Measure(rest.Substring(0, cut), size, bold, mono) > width) cut--;
                    lines.Add(rest.Substring(0, cut));
                    rest = rest.Substring(cut);
                }
                line.Append(rest);
            }
            if (line.Length > 0 || lines.Count == 0) lines.Add(line.ToString());
            return lines;
        }

        public static byte[] Build(ReceiptDocument doc)
        {
            lock (Gate)
            {
                var fonts = LoadFonts();
                _regular = fonts == null ? null : fonts[0];
                _bold = fonts == null ? null : fonts[1];
                try
                {
                    return BuildPage(doc);
                }
                finally
                {
                    _regular = null;
                    _bold = null;
                }
            }
        }

        private static byte[] BuildPage(ReceiptDocument doc)
        {
            var c = new Canvas();
            // Watermark in the bottom right corner, drawn first so the text stays on top of it
            c.Rect(W - Margin - 150, H - 200, 36, 36, 0.93, 0.92, 0.99);
            c.Text("S", W - Margin - 150 + 11.5, H - 200 + 26, 22, "F2", 1, 1, 1);
            c.Text("Sillet", W - Margin - 106, H - 200 + 27, 28, "F2", 0.90, 0.90, 0.95);
            c.Text("raton.fun/wallet", W - Margin - 106, H - 200 + 42, 9, "F1", 0.80, 0.80, 0.88);

            // Header band
            c.Rect(0, 0, W, 150, 0.06, 0.08, 0.16);
            c.Rect(Margin, 40, 30, 30, 0.38, 0.32, 0.96);
            c.Text("S", Margin + 9.5, 61, 20, "F2", 1, 1, 1);
            c.Text(doc.Brand, Margin + 40, 61, 18, "F2", 1, 1, 1);
            c.Text(doc.Heading, Margin, 108, 11, "F1", 0.70, 0.74, 0.85);
            var kindWidth = Measure(doc.Kind, 11, true);
            c.Text(doc.Kind, W - Margin - kindWidth, 61, 11, "F2", 0.70, 0.74, 0.85);

            // Amount card
            var green = doc.Incoming;
            double ar = green ? 0.10 : 0.07, ag = green ? 0.62 : 0.09, ab = green ? 0.38 : 0.16;
            c.Rect(Margin - 8, 168, W - 2 * Margin + 16, 118, 0.96, 0.97, 0.99);
            var amountSize = 30.0;
            while (Measure(doc.Amount, amountSize, true) > W - 2 * Margin - 20 && amountSize > 14) amountSize -= 1;
            c.Text(doc.Amount, (W - Measure(doc.Amount, amountSize, true)) / 2, 222, amountSize, "F2", ar, ag, ab);
            if (!string.IsNullOrEmpty(doc.Fiat))
                c.Text(doc.Fiat, (W - Measure(doc.Fiat, 12, false)) / 2, 244, 12, "F1", 0.42, 0.46, 0.55);
            var pill = doc.Status ?? "";
            var pillW = Measure(pill, 10, true) + 28;
            var pillX = (W - pillW) / 2;
            c.Rect(pillX, 256, pillW, 20, green ? 0.86 : 0.88, green ? 0.96 : 0.93, green ? 0.90 : 0.99);
            c.Text(pill, pillX + 14, 270, 10, "F2", green ? 0.07 : 0.20, green ? 0.45 : 0.30, green ? 0.27 : 0.75);

            // Detail rows
            double y = 322;
            const double labelW = 120;
            double valueX = Margin + labelW + 12;
            double valueW = W - Margin - valueX;
            foreach (var row in doc.Rows)
            {
                if (string.IsNullOrEmpty(row.Value)) continue;
                var mono = doc.Mono.Contains(row.Key);
                var size = mono ? 9.5 : 11;
                var bold = false;
                var lines = Wrap(row.Value, size, bold, valueW, mono);
                var height = lines.Count * (size + 4) + 14;
                if (y + height > H - 90) break;
                c.Text(row.Key, Margin, y + size + 2, 10, "F1", 0.45, 0.49, 0.58);
                double ly = y + size + 2;
                foreach (var line in lines)
                {
                    c.Text(line, valueX, ly, size, mono ? "F3" : "F1", 0.08, 0.10, 0.16);
                    ly += size + 4;
                }
                y += height;
                c.Line(Margin, W - Margin, y - 5, 0.90, 0.92, 0.95);
            }

            // Footer
            c.Line(Margin, W - Margin, H - 76, 0.85, 0.87, 0.92, 0.8);
            foreach (var pair in new[] { new KeyValuePair<string, double>(doc.Footer, H - 58), new KeyValuePair<string, double>(doc.Generated, H - 42) })
            {
                var lines = Wrap(pair.Key ?? "", 8.5, false, W - 2 * Margin);
                var ly = pair.Value;
                foreach (var line in lines) { c.Text(line, Margin, ly, 8.5, "F1", 0.52, 0.55, 0.63); ly += 11; }
            }

            return Assemble(c.Ops.ToString());
        }

        private static byte[] Assemble(string content)
        {
            var latin = System.Text.Encoding.GetEncoding("iso-8859-1");
            var body = latin.GetBytes(content);
            var objects = new List<byte[]>();
            Func<string, byte[]> text = s => latin.GetBytes(s);
            // 1 catalog, 2 pages, 3 page, 4 Courier, 5 content, then the two Arial fonts (5 objects each) or the built in Helvetica pair
            var embedded = _regular != null && _bold != null;
            string f1, f2;
            if (embedded) { f1 = "6 0 R"; f2 = "11 0 R"; } else { f1 = "6 0 R"; f2 = "7 0 R"; }
            objects.Add(text("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(text("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));
            objects.Add(text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(W) + " " + N(H) + "] /Resources << /Font << /F1 " + f1 + " /F2 " + f2 + " /F3 4 0 R >> >> /Contents 5 0 R >>"));
            objects.Add(text("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));
            var stream = new MemoryStream();
            var head = text("<< /Length " + body.Length + " >>\nstream\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            var tail = text("\nendstream");
            stream.Write(tail, 0, tail.Length);
            objects.Add(stream.ToArray());
            if (embedded)
            {
                objects.AddRange(_regular.Objects(6));
                objects.AddRange(_bold.Objects(11));
            }
            else
            {
                objects.Add(text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
                objects.Add(text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
            }

            var output = new MemoryStream();
            Action<byte[]> put = b => output.Write(b, 0, b.Length);
            put(text("%PDF-1.4\n"));
            var offsets = new List<long>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                put(text((i + 1) + " 0 obj\n"));
                put(objects[i]);
                put(text("\nendobj\n"));
            }
            var xref = output.Position;
            put(text("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n"));
            foreach (var offset in offsets) put(text(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
            put(text("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n"));
            return output.ToArray();
        }
    }
}
