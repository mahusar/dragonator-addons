using System;
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
