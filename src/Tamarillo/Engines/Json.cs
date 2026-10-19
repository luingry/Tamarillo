using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Small, order-preserving JSON reader/writer. Numbers keep their original text (no float rounding), so
    /// "format" and "minify" change whitespace only.
    /// </summary>
    internal static class Json
    {
        /// <summary>An object that remembers key order.</summary>
        public sealed class Object
        {
            private readonly List<KeyValuePair<string, object>> _pairs = new List<KeyValuePair<string, object>>();

            public IEnumerable<string> Keys
            {
                get
                {
                    foreach (KeyValuePair<string, object> pair in _pairs)
                    {
                        yield return pair.Key;
                    }
                }
            }

            public IReadOnlyList<KeyValuePair<string, object>> Pairs => _pairs;

            public void Add(string key, object value) => _pairs.Add(new KeyValuePair<string, object>(key, value));

            public bool TryGet(string key, out object value)
            {
                foreach (KeyValuePair<string, object> pair in _pairs)
                {
                    if (pair.Key == key)
                    {
                        value = pair.Value;
                        return true;
                    }
                }
                value = null;
                return false;
            }
        }

        /// <summary>A number kept as its source text.</summary>
        public sealed class Number
        {
            public Number(string text) => Text = text;

            public string Text { get; }

            public override string ToString() => Text;
        }

        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i < text.Length)
            {
                throw new FormatException("JSON inválido na posição " + i + ".");
            }
            return value;
        }

        public static string Write(object value, bool indent)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, indent, 0);
            return builder.ToString();
        }

        /// <summary>Scalar as text for a table cell; containers become compact JSON.</summary>
        public static string Scalar(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case Number n: return n.Text;
                default: return Write(value, indent: false);
            }
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("JSON incompleto.");
            }
            char c = s[i];
            switch (c)
            {
                case '{':
                    i++;
                    var obj = new Object();
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == '}')
                    {
                        i++;
                        return obj;
                    }
                    while (true)
                    {
                        SkipWhitespace(s, ref i);
                        string key = ReadString(s, ref i);
                        SkipWhitespace(s, ref i);
                        Expect(s, ref i, ':');
                        obj.Add(key, ReadValue(s, ref i));
                        SkipWhitespace(s, ref i);
                        if (i < s.Length && s[i] == ',')
                        {
                            i++;
                            continue;
                        }
                        Expect(s, ref i, '}');
                        return obj;
                    }
                case '[':
                    i++;
                    var list = new List<object>();
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == ']')
                    {
                        i++;
                        return list;
                    }
                    while (true)
                    {
                        list.Add(ReadValue(s, ref i));
                        SkipWhitespace(s, ref i);
                        if (i < s.Length && s[i] == ',')
                        {
                            i++;
                            continue;
                        }
                        Expect(s, ref i, ']');
                        return list;
                    }
                case '"':
                    return ReadString(s, ref i);
                case 't':
                    ExpectWord(s, ref i, "true");
                    return true;
                case 'f':
                    ExpectWord(s, ref i, "false");
                    return false;
                case 'n':
                    ExpectWord(s, ref i, "null");
                    return null;
                default:
                    int start = i;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                    {
                        i++;
                    }
                    if (i == start)
                    {
                        throw new FormatException("JSON inválido na posição " + i + ".");
                    }
                    return new Number(s.Substring(start, i - start));
            }
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var builder = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    return builder.ToString();
                }
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        builder.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: builder.Append(e); break;
                }
            }
            throw new FormatException("Texto JSON sem fim.");
        }

        private static void WriteValue(StringBuilder b, object value, bool indent, int depth)
        {
            switch (value)
            {
                case null:
                    b.Append("null");
                    break;
                case bool flag:
                    b.Append(flag ? "true" : "false");
                    break;
                case Number number:
                    b.Append(number.Text);
                    break;
                case string text:
                    WriteString(b, text);
                    break;
                case Object obj:
                    if (obj.Pairs.Count == 0)
                    {
                        b.Append("{}");
                        break;
                    }
                    b.Append('{');
                    for (int k = 0; k < obj.Pairs.Count; k++)
                    {
                        if (k > 0)
                        {
                            b.Append(',');
                        }
                        NewLine(b, indent, depth + 1);
                        WriteString(b, obj.Pairs[k].Key);
                        b.Append(indent ? ": " : ":");
                        WriteValue(b, obj.Pairs[k].Value, indent, depth + 1);
                    }
                    NewLine(b, indent, depth);
                    b.Append('}');
                    break;
                case List<object> list:
                    if (list.Count == 0)
                    {
                        b.Append("[]");
                        break;
                    }
                    b.Append('[');
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0)
                        {
                            b.Append(',');
                        }
                        NewLine(b, indent, depth + 1);
                        WriteValue(b, list[k], indent, depth + 1);
                    }
                    NewLine(b, indent, depth);
                    b.Append(']');
                    break;
                default:
                    WriteString(b, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteString(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            b.Append(c);
                        }
                        break;
                }
            }
            b.Append('"');
        }

        private static void NewLine(StringBuilder b, bool indent, int depth)
        {
            if (indent)
            {
                b.Append("\r\n").Append(' ', depth * 2);
            }
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c)
            {
                throw new FormatException($"JSON inválido: esperado '{c}' na posição {i}.");
            }
            i++;
        }

        private static void ExpectWord(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException("JSON inválido na posição " + i + ".");
            }
            i += word.Length;
        }
    }
}
