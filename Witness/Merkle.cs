using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Dragonator.Addons
{
    public static class Merkle
    {
        public const int DigestBytes = 32;

        public static byte[] Root(List<byte[]> leaves)
        {
            if (leaves == null || leaves.Count == 0) return new byte[DigestBytes];

            List<byte[]> level = new List<byte[]>(leaves);

            while (level.Count > 1) level = NextLevel(level);

            return level[0];
        }

        public static List<string> Path(List<byte[]> leaves, int index)
        {
            List<string> path = new List<string>();

            if (leaves == null || index < 0 || index >= leaves.Count) return path;

            List<byte[]> level = new List<byte[]>(leaves);
            int at = index;

            while (level.Count > 1)
            {
                int sibling = (at % 2 == 0) ? at + 1 : at - 1;
                if (sibling >= level.Count) sibling = at;

                path.Add((at % 2 == 0 ? "r:" : "l:") + Hex(level[sibling]));

                level = NextLevel(level);
                at /= 2;
            }

            return path;
        }

        public static byte[] FollowPath(byte[] leaf, List<string> path)
        {
            if (leaf == null) return null;

            byte[] running = leaf;

            if (path != null)
            {
                foreach (string step in path)
                {
                    if (step == null || step.Length < 3) return null;

                    byte[] other = FromHex(step.Substring(2));
                    if (other == null) return null;

                    running = step.StartsWith("r:")
                        ? Pair(running, other)
                        : Pair(other, running);
                }
            }

            return running;
        }

        private static List<byte[]> NextLevel(List<byte[]> level)
        {
            List<byte[]> next = new List<byte[]>();

            for (int i = 0; i < level.Count; i += 2)
            {
                byte[] left = level[i];
                byte[] right = (i + 1 < level.Count) ? level[i + 1] : level[i];
                next.Add(Pair(left, right));
            }

            return next;
        }

        private static byte[] Pair(byte[] left, byte[] right)
        {
            byte[] joined = new byte[left.Length + right.Length];
            Buffer.BlockCopy(left, 0, joined, 0, left.Length);
            Buffer.BlockCopy(right, 0, joined, left.Length, right.Length);

            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(joined);
        }

        public static string Hex(byte[] bytes)
        {
            if (bytes == null) return "";

            char[] chars = new char[bytes.Length * 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                int value = bytes[i];
                chars[i * 2] = Digit(value >> 4);
                chars[i * 2 + 1] = Digit(value & 0xF);
            }

            return new string(chars);
        }

        public static byte[] FromHex(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.Length & 1) != 0) return null;

            byte[] bytes = new byte[text.Length / 2];

            for (int i = 0; i < bytes.Length; i++)
            {
                int high = Value(text[i * 2]);
                int low = Value(text[i * 2 + 1]);

                if (high < 0 || low < 0) return null;

                bytes[i] = (byte)((high << 4) | low);
            }

            return bytes;
        }

        private static char Digit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }

        private static int Value(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;

            return -1;
        }
    }
}
