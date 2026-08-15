using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dragonator.Addons
{
    internal static class Ledger
    {
        public const string Claimed = "claimed";
        public const string Paid = "paid";
        public const string Held = "held";

        private const string File = "credits.txt";

        private static readonly object guard = new object();
        private static readonly Dictionary<string, string> states =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> notes =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static bool loaded;

        public static string Path
        {
            get { return System.IO.Path.Combine(Paths.Data, File); }
        }

        public static bool Knows(string key)
        {
            lock (guard)
            {
                Load();
                return states.ContainsKey(key);
            }
        }

        public static string StateOf(string key)
        {
            lock (guard)
            {
                Load();

                string state;
                return states.TryGetValue(key, out state) ? state : null;
            }
        }

        public static List<string> Stuck()
        {
            List<string> stuck = new List<string>();

            lock (guard)
            {
                Load();

                foreach (KeyValuePair<string, string> pair in states)
                {
                    if (pair.Value == Paid) continue;

                    string note;
                    notes.TryGetValue(pair.Key, out note);

                    string why = pair.Value == Claimed
                        ? "a send was started and never confirmed - check the wallet by hand before anything else"
                        : note;

                    stuck.Add(Short(pair.Key) + " " + pair.Value +
                              (string.IsNullOrEmpty(why) ? "" : " (" + why + ")"));
                }
            }

            return stuck;
        }

        public static void Write(string key, string state, decimal xmr, decimal xst, string detail)
        {
            lock (guard)
            {
                Load();

                string line = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
                              "|" + key + "|" + state +
                              "|" + Num.Xmr(xmr) + "|" + Num.Xst(xst) +
                              "|" + Flatten(detail);

                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");

                using (FileStream file = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                states[key] = state;
                notes[key] = detail;
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            try
            {
                if (!System.IO.File.Exists(Path)) return;

                foreach (string line in System.IO.File.ReadAllLines(Path))
                {
                    string[] parts = line.Split('|');
                    if (parts.Length < 3) continue;

                    states[parts[1]] = parts[2];
                    notes[parts[1]] = parts.Length > 5 ? parts[5] : null;
                }
            }
            catch (Exception)
            {
            }
        }

        private static string Short(string key)
        {
            return key.Length <= 14 ? key : key.Substring(0, 8) + "..." + key.Substring(key.Length - 4);
        }

        private static string Flatten(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";

            return detail.Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
    }
}
