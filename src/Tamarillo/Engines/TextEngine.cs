using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Text formats handled in-process, no dependency: subtitles (SRT/VTT/TXT), delimited data and JSON
    /// (CSV/TSV/JSON, format/minify), plain text ↔ DOCX/RTF.
    /// </summary>
    internal static class TextEngine
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>Reads text honouring a BOM; falls back to UTF-8, then Windows-1252 if the bytes are not valid UTF-8.</summary>
        public static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)) ||
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), true))
                {
                    return reader.ReadToEnd();
                }
            }
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1252).GetString(bytes);
            }
        }

        public static void WriteText(string path, string text) => File.WriteAllText(path, text, Utf8);

        // ═══ Subtitles ═══

        public sealed class Cue
        {
            public TimeSpan Start;
            public TimeSpan End;
            public string Text;
        }

        private static readonly Regex CueTiming = new Regex(@"(?<s>(\d+:)?\d{1,2}:\d{2}[\.,]\d{1,3})\s*-->\s*(?<e>(\d+:)?\d{1,2}:\d{2}[\.,]\d{1,3})", RegexOptions.Compiled);

        public static List<Cue> ParseSubtitles(string text)
        {
            var cues = new List<Cue>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = CueTiming.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }
                var body = new List<string>();
                int j = i + 1;
                for (; j < lines.Length && lines[j].Trim().Length > 0; j++)
                {
                    body.Add(lines[j].TrimEnd());
                }
                cues.Add(new Cue { Start = ParseTime(match.Groups["s"].Value), End = ParseTime(match.Groups["e"].Value), Text = string.Join("\n", body) });
                i = j;
            }
            return cues;
        }

        private static TimeSpan ParseTime(string value)
        {
            string[] parts = value.Replace(',', '.').Split(':');
            double seconds = double.Parse(parts[parts.Length - 1], CultureInfo.InvariantCulture);
            int minutes = int.Parse(parts[parts.Length - 2], CultureInfo.InvariantCulture);
            int hours = parts.Length > 2 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
            return new TimeSpan(0, hours, minutes, 0).Add(TimeSpan.FromMilliseconds(Math.Round(seconds * 1000)));
        }

        public static string WriteSubtitles(IReadOnlyList<Cue> cues, string format)
        {
            var text = new StringBuilder();
            if (format == "vtt")
            {
                text.Append("WEBVTT\n\n");
            }
            string previous = null;
            for (int i = 0; i < cues.Count; i++)
            {
                Cue cue = cues[i];
                switch (format)
                {
                    case "srt":
                        text.Append(i + 1).Append('\n');
                        text.Append(Time(cue.Start, ',')).Append(" --> ").Append(Time(cue.End, ',')).Append('\n');
                        text.Append(cue.Text).Append("\n\n");
                        break;
                    case "vtt":
                        text.Append(Time(cue.Start, '.')).Append(" --> ").Append(Time(cue.End, '.')).Append('\n');
                        text.Append(cue.Text).Append("\n\n");
                        break;
                    default:
                        string plain = Regex.Replace(cue.Text, "<[^>]+>", string.Empty).Replace('\n', ' ').Trim();
                        if (plain.Length > 0 && plain != previous)
                        {
                            text.Append(plain).Append('\n');
                        }
                        previous = plain;
                        break;
                }
            }
            return text.ToString().Replace("\n", "\r\n");
        }

        private static string Time(TimeSpan t, char separator) =>
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{separator}{t.Milliseconds:000}";

        // ═══ Delimited data ═══

        public static char DetectDelimiter(string text, string ext)
        {
            if (ext == "tsv")
            {
                return '\t';
            }
            string firstLine = text.Split('\n')[0];
            int commas = firstLine.Count(c => c == ','), semicolons = firstLine.Count(c => c == ';'), tabs = firstLine.Count(c => c == '\t');
            return tabs > commas && tabs > semicolons ? '\t' : semicolons > commas ? ';' : ',';
        }

        /// <summary>RFC 4180 parser: quoted fields, doubled quotes, newlines inside quotes.</summary>
        public static List<List<string>> ParseDelimited(string text, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (c == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            rows.RemoveAll(r => r.Count == 1 && r[0].Length == 0);
            return rows;
        }

        public static string WriteDelimited(IEnumerable<IList<string>> rows, char delimiter)
        {
            var text = new StringBuilder();
            foreach (IList<string> row in rows)
            {
                text.Append(string.Join(delimiter.ToString(), row.Select(f => Quote(f ?? string.Empty, delimiter)))).Append("\r\n");
            }
            return text.ToString();
        }

        private static string Quote(string field, char delimiter) =>
            field.IndexOf(delimiter) >= 0 || field.IndexOf('"') >= 0 || field.IndexOf('\n') >= 0 || field.IndexOf('\r') >= 0
                ? "\"" + field.Replace("\"", "\"\"") + "\""
                : field;

        /// <summary>Table (first row = header) → JSON array of objects.</summary>
        public static string TableToJson(List<List<string>> rows)
        {
            if (rows.Count == 0)
            {
                return "[]";
            }
            List<string> header = rows[0];
            var items = new List<object>();
            foreach (List<string> row in rows.Skip(1))
            {
                var obj = new Json.Object();
                for (int c = 0; c < header.Count; c++)
                {
                    obj.Add(header[c], c < row.Count ? row[c] : string.Empty);
                }
                items.Add(obj);
            }
            return Json.Write(items, indent: true);
        }

        /// <summary>JSON array of objects (or a single object) → table; nested values become JSON text.</summary>
        public static List<List<string>> JsonToTable(string json)
        {
            object value = Json.Parse(json);
            List<object> list = value is List<object> array ? array : new List<object> { value };
            var columns = new List<string>();
            foreach (Json.Object obj in list.OfType<Json.Object>())
            {
                foreach (string key in obj.Keys)
                {
                    if (!columns.Contains(key))
                    {
                        columns.Add(key);
                    }
                }
            }
            if (columns.Count == 0)
            {
                columns.Add("valor");
            }
            var rows = new List<List<string>> { columns };
            foreach (object item in list)
            {
                if (item is Json.Object obj)
                {
                    rows.Add(columns.Select(c => obj.TryGet(c, out object v) ? Json.Scalar(v) : string.Empty).ToList());
                }
                else
                {
                    rows.Add(new List<string> { Json.Scalar(item) });
                }
            }
            return rows;
        }

        // ═══ DOCX / RTF ═══

        /// <summary>Plain text → minimal valid DOCX (one paragraph per line).</summary>
        public static void WriteDocx(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                var body = new StringBuilder();
                foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    body.Append(line.Length == 0
                        ? "<w:p/>"
                        : "<w:p><w:r><w:t xml:space=\"preserve\">" + SecurityElement.Escape(StripControl(line)) + "</w:t></w:r></w:p>");
                }
                Entry(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                    body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
            }
        }

        /// <summary>DOCX → plain text (paragraphs, tabs and breaks; no layout).</summary>
        public static string ReadDocx(string path)
        {
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry entry = zip.GetEntry("word/document.xml") ?? throw new Operations.OperationException("Este DOCX não tem conteúdo legível.");
                var text = new StringBuilder();
                using (Stream stream = entry.Open())
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    while (reader.Read())
                    {
                        if (reader.NamespaceURI != w)
                        {
                            continue;
                        }
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            switch (reader.LocalName)
                            {
                                case "t":
                                    text.Append(reader.ReadElementContentAsString());
                                    break;
                                case "tab":
                                    text.Append('\t');
                                    break;
                                case "br":
                                case "cr":
                                    text.Append("\r\n");
                                    break;
                            }
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                        {
                            text.Append("\r\n");
                        }
                    }
                }
                return text.ToString();
            }
        }

        /// <summary>Plain text → RTF with Unicode escapes.</summary>
        public static string WriteRtf(string text)
        {
            var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs22 ");
            foreach (char c in text.Replace("\r\n", "\n"))
            {
                switch (c)
                {
                    case '\\': rtf.Append(@"\\"); break;
                    case '{': rtf.Append(@"\{"); break;
                    case '}': rtf.Append(@"\}"); break;
                    case '\n': rtf.Append(@"\par ").Append("\r\n"); break;
                    case '\t': rtf.Append(@"\tab "); break;
                    default:
                        if (c < 128)
                        {
                            rtf.Append(c);
                        }
                        else
                        {
                            rtf.Append(@"\u").Append((short)c).Append('?');
                        }
                        break;
                }
            }
            return rtf.Append('}').ToString();
        }

        /// <summary>RTF → plain text through WPF's RTF reader. Call on an STA thread.</summary>
        public static string ReadRtf(string path)
        {
            var document = new System.Windows.Documents.FlowDocument();
            var range = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                range.Load(stream, System.Windows.DataFormats.Rtf);
            }
            return range.Text;
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Utf8))
            {
                writer.Write(content);
            }
        }

        private static string StripControl(string s) => new string(s.Where(c => c == '\t' || c >= ' ').ToArray());
    }
}
