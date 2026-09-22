using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Combolands.Mod
{
    // A small JSON document model, for the one file that is not flat.
    //
    // `Json.cs` reads `{"string": "string"}` and nothing else, deliberately - that is
    // the whole grammar a locale file needs, and a reader that cannot be surprised
    // cannot half-load one. The simulator's rule set is a different shape: nested
    // objects, arrays and numbers, dumped from the game once and read back by the
    // plugin, the tests and the trainer alike.
    //
    // So this is a second reader rather than a loosening of the first. It is pure -
    // no Unity, no MelonLoader - because the trainer runs on net8 with no game
    // anywhere near it.
    //
    // Like its sibling it throws on anything it does not understand. A rule set that
    // silently half-loaded would produce a valuation that is confidently wrong, which
    // is worse than one that does not load.
    internal sealed class JsonValue
    {
        internal enum Kind { Null, Bool, Number, String, Array, Object }

        internal Kind Type { get; private set; }

        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;

        // --- reading ---------------------------------------------------------------

        internal static JsonValue Parse(string source)
        {
            if (source == null) throw new FormatException("no JSON to read");

            int at = 0;
            var value = ReadValue(source, ref at);

            SkipWhitespace(source, ref at);
            if (at != source.Length)
                throw new FormatException(Where(source, at) + ": trailing content after the document");

            return value;
        }

        // --- looking at it ---------------------------------------------------------

        internal int Count
        {
            get
            {
                if (Type == Kind.Array) return _array.Count;
                if (Type == Kind.Object) return _object.Count;
                return 0;
            }
        }

        internal IEnumerable<KeyValuePair<string, JsonValue>> Fields
        {
            get
            {
                if (Type != Kind.Object) yield break;
                foreach (var pair in _object) yield return pair;
            }
        }

        internal IEnumerable<JsonValue> Items
        {
            get
            {
                if (Type != Kind.Array) yield break;
                foreach (var item in _array) yield return item;
            }
        }

        // Absent and null read the same, because for this data they mean the same
        // thing: the dump had nothing to say about that field.
        internal JsonValue this[string field]
        {
            get
            {
                JsonValue found;
                if (Type == Kind.Object && _object.TryGetValue(field, out found)) return found;
                return null;
            }
        }

        internal string AsString(string fallback = null)
        {
            return Type == Kind.String ? _string : fallback;
        }

        internal double AsNumber(double fallback = 0)
        {
            return Type == Kind.Number ? _number : fallback;
        }

        internal int AsInt(int fallback = 0)
        {
            if (Type != Kind.Number) return fallback;

            // The dump writes integers as integers; a fractional value here means the
            // file is not what this code thinks it is.
            var rounded = Math.Round(_number);
            if (Math.Abs(_number - rounded) > 1e-9)
                throw new FormatException("expected a whole number, found " + _number);

            return (int)rounded;
        }

        internal float AsFloat(float fallback = 0f)
        {
            return Type == Kind.Number ? (float)_number : fallback;
        }

        internal bool AsBool(bool fallback = false)
        {
            return Type == Kind.Bool ? _bool : fallback;
        }

        // --- the reader ------------------------------------------------------------

        private static JsonValue ReadValue(string src, ref int at)
        {
            SkipWhitespace(src, ref at);
            if (at >= src.Length) throw new FormatException("the document ends where a value should be");

            switch (src[at])
            {
                case '{': return ReadObject(src, ref at);
                case '[': return ReadArray(src, ref at);
                case '"': return new JsonValue { Type = Kind.String, _string = ReadString(src, ref at) };
                case 't': Literal(src, ref at, "true");  return new JsonValue { Type = Kind.Bool, _bool = true };
                case 'f': Literal(src, ref at, "false"); return new JsonValue { Type = Kind.Bool, _bool = false };
                case 'n': Literal(src, ref at, "null");  return new JsonValue { Type = Kind.Null };
                default:  return ReadNumber(src, ref at);
            }
        }

        private static JsonValue ReadObject(string src, ref int at)
        {
            var value = new JsonValue
            {
                Type = Kind.Object,
                _object = new Dictionary<string, JsonValue>(StringComparer.Ordinal),
            };

            at++;                                   // past '{'
            SkipWhitespace(src, ref at);
            if (Peek(src, at) == '}') { at++; return value; }

            while (true)
            {
                SkipWhitespace(src, ref at);
                var name = ReadString(src, ref at);

                SkipWhitespace(src, ref at);
                Expect(src, ref at, ':');

                // A duplicate key means two answers to one question, and picking
                // either is a guess.
                if (value._object.ContainsKey(name))
                    throw new FormatException(Where(src, at) + ": \"" + name + "\" appears twice");

                value._object[name] = ReadValue(src, ref at);

                SkipWhitespace(src, ref at);
                if (Peek(src, at) == ',') { at++; continue; }

                Expect(src, ref at, '}');
                return value;
            }
        }

        private static JsonValue ReadArray(string src, ref int at)
        {
            var value = new JsonValue { Type = Kind.Array, _array = new List<JsonValue>() };

            at++;                                   // past '['
            SkipWhitespace(src, ref at);
            if (Peek(src, at) == ']') { at++; return value; }

            while (true)
            {
                value._array.Add(ReadValue(src, ref at));

                SkipWhitespace(src, ref at);
                if (Peek(src, at) == ',') { at++; continue; }

                Expect(src, ref at, ']');
                return value;
            }
        }

        private static JsonValue ReadNumber(string src, ref int at)
        {
            int start = at;
            if (Peek(src, at) == '-') at++;

            while (at < src.Length && (char.IsDigit(src[at]) || src[at] == '.'
                                       || src[at] == 'e' || src[at] == 'E'
                                       || src[at] == '+' || src[at] == '-')) at++;

            var text = src.Substring(start, at - start);

            double parsed;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                throw new FormatException(Where(src, start) + ": \"" + text + "\" is not a number");

            return new JsonValue { Type = Kind.Number, _number = parsed };
        }

        private static string ReadString(string src, ref int at)
        {
            Expect(src, ref at, '"');

            var text = new StringBuilder();
            while (true)
            {
                if (at >= src.Length) throw new FormatException("the document ends inside a string");

                var c = src[at++];
                if (c == '"') return text.ToString();

                if (c != '\\') { text.Append(c); continue; }

                if (at >= src.Length) throw new FormatException("the document ends inside an escape");

                var escape = src[at++];
                switch (escape)
                {
                    case '"':  text.Append('"');  break;
                    case '\\': text.Append('\\'); break;
                    case '/':  text.Append('/');  break;
                    case 'b':  text.Append('\b'); break;
                    case 'f':  text.Append('\f'); break;
                    case 'n':  text.Append('\n'); break;
                    case 'r':  text.Append('\r'); break;
                    case 't':  text.Append('\t'); break;
                    case 'u':
                        if (at + 4 > src.Length) throw new FormatException("a \\u escape runs off the end");
                        text.Append((char)Convert.ToInt32(src.Substring(at, 4), 16));
                        at += 4;
                        break;
                    default:
                        throw new FormatException(Where(src, at - 1) + ": unknown escape \\" + escape);
                }
            }
        }

        // --- writing ---------------------------------------------------------------

        // Only what the dump needs: strings, numbers and the punctuation around them.
        // The dumper builds the document itself rather than modelling it, because a
        // writer that can produce anything is a writer whose output nothing checks.
        internal static string Quote(string text)
        {
            var quoted = new StringBuilder(text.Length + 2);
            quoted.Append('"');

            foreach (var c in text)
            {
                switch (c)
                {
                    case '"':  quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\b': quoted.Append("\\b");  break;
                    case '\f': quoted.Append("\\f");  break;
                    case '\n': quoted.Append("\\n");  break;
                    case '\r': quoted.Append("\\r");  break;
                    case '\t': quoted.Append("\\t");  break;
                    default:
                        if (c < 0x20) quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else quoted.Append(c);
                        break;
                }
            }

            quoted.Append('"');
            return quoted.ToString();
        }

        internal static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // --- plumbing --------------------------------------------------------------

        private static void SkipWhitespace(string src, ref int at)
        {
            while (at < src.Length && (src[at] == ' ' || src[at] == '\t'
                                       || src[at] == '\r' || src[at] == '\n')) at++;
        }

        private static char Peek(string src, int at)
        {
            return at < src.Length ? src[at] : '\0';
        }

        private static void Expect(string src, ref int at, char c)
        {
            SkipWhitespace(src, ref at);
            if (Peek(src, at) != c)
                throw new FormatException(Where(src, at) + ": expected '" + c + "'");
            at++;
        }

        private static void Literal(string src, ref int at, string word)
        {
            if (at + word.Length > src.Length || src.Substring(at, word.Length) != word)
                throw new FormatException(Where(src, at) + ": expected " + word);
            at += word.Length;
        }

        // Line and column, because "unexpected character at 48213" is not a message
        // anyone can act on.
        private static string Where(string src, int at)
        {
            int line = 1, column = 1;
            for (int i = 0; i < at && i < src.Length; i++)
            {
                if (src[i] == '\n') { line++; column = 1; }
                else column++;
            }
            return "line " + line + ", column " + column;
        }
    }
}
