using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dragonator.Swapper
{
    internal static class Json
    {
        public static int KeyAt(string json, int from, string key)
        {
            if (json == null || from < 0) return -1;

            return json.IndexOf("\"" + key + "\"", from, StringComparison.Ordinal);
        }

        public static int ValueAt(string json, int from, string key)
        {
            int at = KeyAt(json, from, key);
            if (at < 0) return -1;

            at += key.Length + 2;

            while (at < json.Length && (json[at] == ' ' || json[at] == ':' || json[at] == '\t')) at++;

            return at >= json.Length ? -1 : at;
        }

        public static bool TryNumber(string json, int from, string key, out decimal value)
        {
            value = 0m;

            int at = ValueAt(json, from, key);
            if (at < 0) return false;

            if (json[at] == '"' || json[at] == '[')
            {
                int quote = json.IndexOf('"', json[at] == '"' ? at : at + 1);
                if (quote < 0) return false;

                int end = json.IndexOf('"', quote + 1);
                if (end < 0) return false;

                return decimal.TryParse(json.Substring(quote + 1, end - quote - 1), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out value);
            }

            int stop = at;
            while (stop < json.Length && "+-.0123456789eE".IndexOf(json[stop]) >= 0) stop++;

            return stop > at && decimal.TryParse(json.Substring(at, stop - at), NumberStyles.Float,
                                                 CultureInfo.InvariantCulture, out value);
        }

        public static bool TryLong(string json, int from, string key, out long value)
        {
            value = 0L;

            decimal parsed;
            if (!TryNumber(json, from, key, out parsed)) return false;

            if (parsed < long.MinValue || parsed > long.MaxValue) return false;

            value = (long)parsed;
            return true;
        }

        public static bool TrueAt(string json, int from, string key)
        {
            int at = ValueAt(json, from, key);
            if (at < 0) return false;

            return string.CompareOrdinal(json, at, "true", 0, 4) == 0;
        }

        public static string Field(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int at = json.IndexOf('{');
            if (at < 0) return null;

            at++;

            while (at < json.Length)
            {
                at = Space(json, at);
                if (at >= json.Length || json[at] == '}') return null;

                if (json[at] != '"') return null;

                int nameEnd = EndOfText(json, at);
                if (nameEnd < 0) return null;

                string name = json.Substring(at + 1, nameEnd - at - 1);

                at = Space(json, nameEnd + 1);
                if (at >= json.Length || json[at] != ':') return null;

                at = Space(json, at + 1);

                int valueEnd = EndOfValue(json, at);
                if (valueEnd < 0) return null;

                if (name == key) return json.Substring(at, valueEnd - at);

                at = Space(json, valueEnd);
                if (at < json.Length && json[at] == ',') at++;
            }

            return null;
        }

        public static List<string> Items(string json, string key)
        {
            List<string> items = new List<string>();

            string raw = Field(json, key);
            if (string.IsNullOrEmpty(raw) || raw[0] != '[') return items;

            int at = 1;

            while (at < raw.Length)
            {
                at = Space(raw, at);
                if (at >= raw.Length || raw[at] == ']') break;

                int end = EndOfValue(raw, at);
                if (end < 0) break;

                items.Add(raw.Substring(at, end - at));

                at = Space(raw, end);
                if (at < raw.Length && raw[at] == ',') at++;
            }

            return items;
        }

        public static string TextOf(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (raw[0] != '"') return raw;

            int end = EndOfText(raw, 0);
            return end < 0 ? null : raw.Substring(1, end - 1);
        }

        public static bool TryDecimalOf(string raw, out decimal value)
        {
            value = 0m;

            string text = TextOf(raw);
            if (string.IsNullOrEmpty(text)) return false;

            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryLongOf(string raw, out long value)
        {
            value = 0L;

            decimal parsed;
            if (!TryDecimalOf(raw, out parsed)) return false;
            if (parsed < long.MinValue || parsed > long.MaxValue) return false;

            value = (long)parsed;
            return true;
        }

        private static int Space(string json, int at)
        {
            while (at < json.Length && (json[at] == ' ' || json[at] == '\t' ||
                                       json[at] == '\r' || json[at] == '\n')) at++;

            return at;
        }

        private static int EndOfText(string json, int quote)
        {
            for (int i = quote + 1; i < json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    i++;
                    continue;
                }

                if (json[i] == '"') return i;
            }

            return -1;
        }

        private static int EndOfValue(string json, int at)
        {
            if (at >= json.Length) return -1;

            char first = json[at];

            if (first == '"')
            {
                int end = EndOfText(json, at);
                return end < 0 ? -1 : end + 1;
            }

            if (first == '{' || first == '[')
            {
                char close = first == '{' ? '}' : ']';
                int depth = 0;

                for (int i = at; i < json.Length; i++)
                {
                    char c = json[i];

                    if (c == '"')
                    {
                        i = EndOfText(json, i);
                        if (i < 0) return -1;

                        continue;
                    }

                    if (c == first) depth++;
                    else if (c == close && --depth == 0) return i + 1;
                }

                return -1;
            }

            int stop = at;
            while (stop < json.Length && ",}] \t\r\n".IndexOf(json[stop]) < 0) stop++;

            return stop == at ? -1 : stop;
        }

        public static int FlatObjectStart(string json, int inside)
        {
            for (int i = inside; i >= 0; i--)
                if (json[i] == '{') return i;

            return -1;
        }

        public static int FlatObjectEnd(string json, int inside)
        {
            for (int i = inside; i < json.Length; i++)
                if (json[i] == '}') return i;

            return -1;
        }
    }
}
