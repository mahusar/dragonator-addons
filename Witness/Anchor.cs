using System;
using System.Collections.Generic;

namespace Dragonator.Addons
{
    public class Anchor
    {
        public const string Magic = "58535457";
        public const int Version = 1;
        public const int RecordBytes = 40;
        public const int HexLength = RecordBytes * 2;

        public const int FlagContested = 0x01;
        public const int FlagBot = 0x02;

        public byte[] Root;
        public int Count;
        public int Flags;

        public bool Contested { get { return (Flags & FlagContested) != 0; } }

        public bool AnyBot { get { return (Flags & FlagBot) != 0; } }

        public string Describe()
        {
            string text = Contested && !AnyBot
                ? "every match human against human"
                : "not all matches are contested";

            if (AnyBot) text += ", at least one has a bot";

            return text;
        }

        public static string Encode(byte[] root, int count, int flags)
        {
            if (root == null || root.Length != Merkle.DigestBytes)
                throw new ArgumentException("a witness anchor needs a 32 byte merkle root");

            if (count < 0 || count > ushort.MaxValue)
                throw new ArgumentException("a witness anchor holds at most " + ushort.MaxValue + " receipts");

            System.Text.StringBuilder sb = new System.Text.StringBuilder(HexLength);

            sb.Append(Magic);
            sb.Append(Version.ToString("x2"));
            sb.Append(Merkle.Hex(root));
            sb.Append(count.ToString("x4"));
            sb.Append((flags & 0xFF).ToString("x2"));

            return sb.ToString();
        }

        public static Anchor Decode(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != HexLength) return null;
            if (!hex.StartsWith(Magic, StringComparison.OrdinalIgnoreCase)) return null;

            byte[] bytes = Merkle.FromHex(hex);
            if (bytes == null || bytes.Length != RecordBytes) return null;
            if (bytes[4] != Version) return null;

            byte[] root = new byte[Merkle.DigestBytes];
            Buffer.BlockCopy(bytes, 5, root, 0, Merkle.DigestBytes);

            return new Anchor
            {
                Root = root,
                Count = (bytes[37] << 8) | bytes[38],
                Flags = bytes[39]
            };
        }

        public static List<Anchor> ScanBlock(string json)
        {
            List<Anchor> found = new List<Anchor>();

            if (string.IsNullOrEmpty(json)) return found;

            foreach (string push in ScriptScan.NullDataPushes(json))
            {
                if (push.Length != HexLength) continue;

                Anchor anchor = Decode(push);
                if (anchor != null) found.Add(anchor);
            }

            return found;
        }

        public bool Covers(string receiptDigestHex, List<string> path)
        {
            byte[] leaf = Merkle.FromHex(receiptDigestHex);
            if (leaf == null || leaf.Length != Merkle.DigestBytes) return false;

            byte[] reached = Merkle.FollowPath(leaf, path);
            if (reached == null || Root == null) return false;

            if (reached.Length != Root.Length) return false;

            int difference = 0;
            for (int i = 0; i < Root.Length; i++) difference |= reached[i] ^ Root[i];

            return difference == 0;
        }
    }
}
