using System;
using System.Collections.Generic;
using System.IO;

namespace Dragonator.Addons
{
    public static class ReceiptStore
    {
        private const string Folder = "receipts";

        public static string Root
        {
            get { return Path.Combine(Paths.Data, Folder); }
        }

        public static void Save(string digest, string receipt, string signatures)
        {
            if (string.IsNullOrEmpty(digest) || string.IsNullOrEmpty(receipt)) return;

            Directory.CreateDirectory(Root);

            List<string> lines = new List<string>();
            lines.Add(receipt);
            lines.Add("");
            lines.Add("signatures=" + (signatures ?? ""));

            File.WriteAllLines(FileFor(digest), lines);
        }

        public static void Anchored(string digest, string txid, List<string> path)
        {
            if (string.IsNullOrEmpty(digest)) return;

            string file = FileFor(digest);
            if (!File.Exists(file)) return;

            List<string> lines = new List<string>(File.ReadAllLines(file));
            lines.RemoveAll(line => line.StartsWith("txid=") || line.StartsWith("proof="));

            lines.Add("txid=" + (txid ?? ""));
            lines.Add("proof=" + (path != null ? string.Join(";", path.ToArray()) : ""));

            File.WriteAllLines(file, lines);
        }

        public static string Read(string digest)
        {
            if (!Clean(digest)) return "";

            string file = FileFor(digest);

            return File.Exists(file) ? File.ReadAllText(file) : "";
        }

        public static bool Has(string digest)
        {
            return Clean(digest) && File.Exists(FileFor(digest));
        }

        public static List<string> Unanchored()
        {
            List<string> waiting = new List<string>();

            if (!Directory.Exists(Root)) return waiting;

            string[] files;
            try
            {
                files = Directory.GetFiles(Root, "*.txt");
            }
            catch (Exception)
            {
                return waiting;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string digest = Path.GetFileNameWithoutExtension(file);
                if (!Clean(digest)) continue;

                bool anchored = false;
                try
                {
                    foreach (string line in File.ReadAllLines(file))
                    {
                        if (!line.StartsWith("txid=")) continue;
                        if (line.Length > 5) anchored = true;
                        break;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                if (!anchored) waiting.Add(digest.ToLowerInvariant());
            }

            return waiting;
        }

        private static string FileFor(string digest)
        {
            return Path.Combine(Root, digest.ToLowerInvariant() + ".txt");
        }

        private static bool Clean(string digest)
        {
            if (string.IsNullOrEmpty(digest) || digest.Length != 64) return false;

            foreach (char c in digest)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }

            return true;
        }
    }
}
