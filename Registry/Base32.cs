using System;
using System.Text;

namespace Dragonator.Addons
{
    internal static class Base32
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0) return "";

            StringBuilder sb = new StringBuilder();

            int buffer = 0;
            int bits = 0;

            foreach (byte value in data)
            {
                buffer = (buffer << 8) | value;
                bits += 8;

                while (bits >= 5)
                {
                    sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);

            return sb.ToString();
        }

        public static byte[] Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string lower = text.ToLowerInvariant();
            byte[] output = new byte[lower.Length * 5 / 8];

            int buffer = 0;
            int bits = 0;
            int written = 0;

            foreach (char c in lower)
            {
                int index = Alphabet.IndexOf(c);
                if (index < 0) return null;

                buffer = (buffer << 5) | index;
                bits += 5;

                if (bits < 8) continue;

                output[written++] = (byte)((buffer >> (bits - 8)) & 255);
                bits -= 8;
            }

            return written == output.Length ? output : null;
        }

        public static string ToHex(byte[] data)
        {
            if (data == null) return "";

            StringBuilder sb = new StringBuilder(data.Length * 2);
            foreach (byte value in data) sb.Append(value.ToString("x2"));

            return sb.ToString();
        }

        public static byte[] FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return null;

            byte[] output = new byte[hex.Length / 2];

            for (int i = 0; i < output.Length; i++)
            {
                int high = HexValue(hex[i * 2]);
                int low = HexValue(hex[i * 2 + 1]);

                if (high < 0 || low < 0) return null;

                output[i] = (byte)((high << 4) | low);
            }

            return output;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;

            return -1;
        }
    }
}
