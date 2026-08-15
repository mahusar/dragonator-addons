using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Dragonator.Addons
{
    public static class Merkle
    {
        public const int DigestBytes = 32;

        private const byte LeafPrefix = 0x00;
        private const byte NodePrefix = 0x01;

        public static byte[] LeafHash(byte[] digest)
        {
            if (digest == null) return null;

            byte[] buffer = new byte[1 + digest.Length];
            buffer[0] = LeafPrefix;
            Buffer.BlockCopy(digest, 0, buffer, 1, digest.Length);

            return Sha256(buffer);
        }

        public static byte[] NodeHash(byte[] left, byte[] right)
        {
            if (left == null || right == null) return null;

            byte[] buffer = new byte[1 + left.Length + right.Length];
            buffer[0] = NodePrefix;
            Buffer.BlockCopy(left, 0, buffer, 1, left.Length);
            Buffer.BlockCopy(right, 0, buffer, 1 + left.Length, right.Length);

            return Sha256(buffer);
        }

        public static byte[] Root(List<byte[]> leaves)
        {
            if (leaves == null || leaves.Count == 0) return Sha256(new byte[0]);

            return RootOf(leaves, 0, leaves.Count);
        }

        public static List<string> Path(List<byte[]> leaves, int index)
        {
            List<string> path = new List<string>();

            if (leaves == null || index < 0 || index >= leaves.Count) return path;

            Walk(leaves, 0, leaves.Count, index, path);

            return path;
        }

        public static byte[] FollowPath(byte[] leafDigest, List<string> path)
        {
            if (leafDigest == null) return null;

            byte[] running = LeafHash(leafDigest);

            if (path != null)
            {
                foreach (string step in path)
                {
                    if (step == null || step.Length < 3) return null;

                    byte[] other = FromHex(step.Substring(2));
                    if (other == null || other.Length != DigestBytes) return null;

                    if (step.StartsWith("r:", StringComparison.Ordinal)) running = NodeHash(running, other);
                    else if (step.StartsWith("l:", StringComparison.Ordinal)) running = NodeHash(other, running);
                    else return null;
                }
            }

            return running;
        }

        private static byte[] RootOf(List<byte[]> leaves, int start, int count)
        {
            if (count == 1) return LeafHash(leaves[start]);

            int split = SplitAt(count);

            return NodeHash(RootOf(leaves, start, split),
                            RootOf(leaves, start + split, count - split));
        }

        private static void Walk(List<byte[]> leaves, int start, int count, int index, List<string> path)
        {
            if (count == 1) return;

            int split = SplitAt(count);

            if (index < split)
            {
                Walk(leaves, start, split, index, path);
                path.Add("r:" + Hex(RootOf(leaves, start + split, count - split)));
            }
            else
            {
                Walk(leaves, start + split, count - split, index - split, path);
                path.Add("l:" + Hex(RootOf(leaves, start, split)));
            }
        }

        private static int SplitAt(int count)
        {
            int split = 1;
            while (split * 2 < count) split *= 2;

            return split;
        }

        private static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
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
