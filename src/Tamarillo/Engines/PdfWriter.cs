using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Minimal PDF 1.4 writer: pages of JPEG images (embedded as-is, DCTDecode) and pages of plain text in
    /// Courier (a standard font, nothing embedded). Enough for "images → PDF" and "text → PDF" without any
    /// dependency. Objects are streamed to disk; only the xref offsets are kept in memory.
    /// </summary>
    internal sealed class PdfWriter : IDisposable
    {
        public const double A4Width = 595.28, A4Height = 841.89;

        private readonly Stream _stream;
        private readonly List<long> _offsets = new List<long> { 0 };
        private readonly List<int> _pages = new List<int>();
        private readonly int _pagesId;
        private int _fontId;

        public PdfWriter(Stream stream)
        {
            _stream = stream;
            WriteRaw("%PDF-1.4\n%âãÏÓ\n", Latin1);
            _pagesId = Reserve();
        }

        private static Encoding Latin1 { get; } = Encoding.GetEncoding(28591);

        private static Encoding WinAnsi { get; } = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        /// <summary>Adds a page showing a JPEG at the page's full size.</summary>
        public void AddJpegPage(byte[] jpeg, int pixelWidth, int pixelHeight, bool grayscale, double pageWidth, double pageHeight)
        {
            int imageId = BeginObject();
            WriteAscii($"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} /ColorSpace /{(grayscale ? "DeviceGray" : "DeviceRGB")} " +
                       $"/BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            _stream.Write(jpeg, 0, jpeg.Length);
            WriteAscii("\nendstream\nendobj\n");

            string content = $"q {N(pageWidth)} 0 0 {N(pageHeight)} 0 0 cm /Im0 Do Q";
            int contentId = WriteStreamObject(Encoding.ASCII.GetBytes(content));
            AddPage(pageWidth, pageHeight, $"/XObject << /Im0 {imageId} 0 R >>", contentId);
        }

        /// <summary>Lays out plain text on A4 pages (Courier 10/12.5, 2 cm margins, hard-wrapped).</summary>
        public void AddText(string text, Action<double> progress = null)
        {
            const double fontSize = 10, leading = 12.5, margin = 56.7;
            int charsPerLine = (int)((A4Width - 2 * margin) / (fontSize * 0.6));
            int linesPerPage = (int)((A4Height - 2 * margin) / leading);

            var lines = new List<string>();
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Replace("\t", "    ");
                if (line.Length == 0)
                {
                    lines.Add(string.Empty);
                }
                while (line.Length > 0)
                {
                    int cut = Math.Min(charsPerLine, line.Length);
                    if (cut < line.Length)
                    {
                        int space = line.LastIndexOf(' ', cut - 1, cut);
                        if (space > charsPerLine / 2)
                        {
                            cut = space + 1;
                        }
                    }
                    lines.Add(line.Substring(0, cut).TrimEnd());
                    line = line.Substring(cut);
                }
            }
            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            if (_fontId == 0)
            {
                _fontId = BeginObject();
                WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n");
            }

            for (int start = 0; start < lines.Count; start += linesPerPage)
            {
                var content = new MemoryStream();
                void Put(string s)
                {
                    byte[] bytes = WinAnsi.GetBytes(s);
                    content.Write(bytes, 0, bytes.Length);
                }
                Put($"BT /F1 {N(fontSize)} Tf {N(leading)} TL {N(margin)} {N(A4Height - margin - fontSize)} Td\n");
                for (int i = start; i < Math.Min(lines.Count, start + linesPerPage); i++)
                {
                    Put("(" + Escape(lines[i]) + ") Tj T*\n");
                }
                Put("ET");
                int contentId = WriteStreamObject(content.ToArray());
                AddPage(A4Width, A4Height, $"/Font << /F1 {_fontId} 0 R >>", contentId);
                progress?.Invoke(Math.Min(1.0, (double)(start + linesPerPage) / lines.Count));
            }
        }

        public void Dispose()
        {
            // Page tree, catalog, xref, trailer.
            Begin(_pagesId);
            var kids = new StringBuilder();
            foreach (int page in _pages)
            {
                kids.Append(page).Append(" 0 R ");
            }
            WriteAscii($"<< /Type /Pages /Kids [ {kids}] /Count {_pages.Count} >>\nendobj\n");
            int catalogId = BeginObject();
            WriteAscii($"<< /Type /Catalog /Pages {_pagesId} 0 R >>\nendobj\n");
            int infoId = BeginObject();
            WriteAscii($"<< /Producer (Tamarillo) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>\nendobj\n");

            long xref = _stream.Position;
            var table = new StringBuilder();
            table.Append("xref\n0 ").Append(_offsets.Count).Append('\n');
            table.Append("0000000000 65535 f \n");
            for (int i = 1; i < _offsets.Count; i++)
            {
                table.Append(_offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }
            table.Append($"trailer\n<< /Size {_offsets.Count} /Root {catalogId} 0 R /Info {infoId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            WriteAscii(table.ToString());
            _stream.Flush();
        }

        private void AddPage(double width, double height, string resources, int contentId)
        {
            int pageId = BeginObject();
            WriteAscii($"<< /Type /Page /Parent {_pagesId} 0 R /MediaBox [0 0 {N(width)} {N(height)}] /Resources << {resources} >> /Contents {contentId} 0 R >>\nendobj\n");
            _pages.Add(pageId);
        }

        private int WriteStreamObject(byte[] data)
        {
            int id = BeginObject();
            WriteAscii($"<< /Length {data.Length} >>\nstream\n");
            _stream.Write(data, 0, data.Length);
            WriteAscii("\nendstream\nendobj\n");
            return id;
        }

        private int Reserve()
        {
            _offsets.Add(-1);
            return _offsets.Count - 1;
        }

        private int BeginObject()
        {
            int id = Reserve();
            Begin(id);
            return id;
        }

        private void Begin(int id)
        {
            _offsets[id] = _stream.Position;
            WriteAscii(id + " 0 obj\n");
        }

        private void WriteAscii(string s) => WriteRaw(s, Encoding.ASCII);

        private void WriteRaw(string s, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(s);
            _stream.Write(bytes, 0, bytes.Length);
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
