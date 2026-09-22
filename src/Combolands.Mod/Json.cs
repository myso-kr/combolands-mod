using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Combolands.Mod
{
    // A flat {"string": "string"} reader and writer, and nothing else.
    //
    // The game ships no JSON library that can do a dictionary - Unity's JsonUtility
    // cannot - and pulling one in would put a second copy of it in the process. The
    // locale file is a flat object of string pairs, so this is the whole grammar we
    // need.
    //
    // It throws on anything it does not understand rather than skipping it. A locale
    // file that silently half-loaded would show up as scattered untranslated strings,
    // which is the most expensive kind of bug to chase.
    internal static class Json
    {
        internal static Dictionary<string, string> ReadFlatObject(string src)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;

            SkipWs(src, ref i);
            Expect(src, ref i, '{');
            SkipWs(src, ref i);

            if (Peek(src, i) == '}') { i++; return map; }

            while (true)
            {
                SkipWs(src, ref i);
                var key = ReadString(src, ref i);
                SkipWs(src, ref i);
                Expect(src, ref i, ':');
                SkipWs(src, ref i);
                var value = ReadString(src, ref i);
                map[key] = value;

                SkipWs(src, ref i);
                var c = Peek(src, i);
                if (c == ',') { i++; continue; }
                if (c == '}') { i++; break; }
                throw Bad(i, "expected ',' or '}' but found '" + c + "'");
            }

            SkipWs(src, ref i);
            if (i != src.Length) throw Bad(i, "trailing content after the object");
            return map;
        }

        internal static string WriteFlatObject(SortedDictionary<string, string> map)
        {
            var sb = new StringBuilder("{\n");
            int n = 0;
            foreach (var kv in map)
            {
                sb.Append("  ").Append(Quote(kv.Key)).Append(": ").Append(Quote(kv.Value));
                sb.Append(++n < map.Count ? ",\n" : "\n");
            }
            return sb.Append("}\n").ToString();
        }

        // --- reading --------------------------------------------------------------

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw Bad(i, "unterminated string");
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) throw Bad(i, "unterminated escape");
                var e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Bad(i, "truncated \\u escape");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw Bad(i, "unknown escape '\\" + e + "'");
                }
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static char Peek(string s, int i)
        {
            return i < s.Length ? s[i] : '\0';
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c)
                throw Bad(i, "expected '" + c + "' but found " + (i >= s.Length ? "end of file" : "'" + s[i] + "'"));
            i++;
        }

        private static FormatException Bad(int i, string why)
        {
            return new FormatException("locale JSON at offset " + i + ": " + why);
        }

        // --- writing --------------------------------------------------------------

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
