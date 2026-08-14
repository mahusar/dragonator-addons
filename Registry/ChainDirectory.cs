using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Dragonator.Addons
{
    public class ChainDirectory : IServerDirectory
    {
        private const int BlocksPerPass = 250;
        private const int IdleSeconds = 60;
        private const int RetrySeconds = 120;
        private const string StoreFile = "registry.txt";

        private static readonly object Gate = new object();
        private static readonly List<string> Entries = new List<string>();

        private static Thread worker;
        private static long scanned;
        private static long tip;
        private static string problem;
        private static bool restored;

        public string Name { get { return "registry"; } }

        public string Status { get { return Describe(); } }

        public List<string> Listings
        {
            get
            {
                if (!RegistryState.Enabled) return new List<string>();

                Begin();

                lock (Gate) return new List<string>(Entries);
            }
        }

        public static string Describe()
        {
            lock (Gate)
            {
                if (!RegistryState.Enabled) return "off";
                if (problem != null) return problem;

                string count = Entries.Count.ToString(CultureInfo.InvariantCulture) +
                               (Entries.Count == 1 ? " server" : " servers");

                if (tip > 0 && scanned > 0 && scanned < tip)
                    return count + ", " + (tip - scanned).ToString("N0", CultureInfo.InvariantCulture) +
                           " blocks behind";

                return count;
            }
        }

        internal static void Begin()
        {
            lock (Gate)
            {
                if (worker != null) return;

                Restore();

                worker = new Thread(Run);
                worker.IsBackground = true;
                worker.Start();
            }
        }

        private static void Run()
        {
            while (true)
            {
                int wait = IdleSeconds;

                try
                {
                    Sweep();

                    lock (Gate) problem = null;
                }
                catch (Exception e)
                {
                    lock (Gate) problem = Shorten(e.Message);
                    wait = RetrySeconds;
                }

                Thread.Sleep(wait * 1000);
            }
        }

        private static void Sweep()
        {
            long height = Chain.Height();

            long from;

            lock (Gate)
            {
                tip = height;
                from = scanned <= 0 ? RegistryState.StartHeight - 1 : scanned;
            }

            while (from < height)
            {
                long stop = from + BlocksPerPass;
                if (stop > height) stop = height;

                for (long at = from + 1; at <= stop; at++)
                {
                    string block = Chain.Block(Chain.HashAt(at));

                    foreach (Listing listing in Listing.ScanBlock(block)) Remember(listing);
                }

                from = stop;

                lock (Gate) scanned = from;

                Save();

                height = Chain.Height();

                lock (Gate) tip = height;
            }
        }

        private static void Remember(Listing listing)
        {
            string prefix = listing.Onion + ":";

            lock (Gate)
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (!Entries[i].StartsWith(prefix, StringComparison.Ordinal)) continue;

                    Entries[i] = listing.Entry;
                    return;
                }

                Entries.Add(listing.Entry);
            }
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();

                lock (Gate)
                {
                    lines.Add("height " + scanned.ToString(CultureInfo.InvariantCulture));

                    foreach (string entry in Entries) lines.Add("server " + entry);
                }

                File.WriteAllLines(Path.Combine(Paths.Data, StoreFile), lines.ToArray());
            }
            catch (Exception)
            {
            }
        }

        private static void Restore()
        {
            if (restored) return;
            restored = true;

            string file = Path.Combine(Paths.Data, StoreFile);

            string[] lines;

            try
            {
                if (!File.Exists(file)) return;

                lines = File.ReadAllLines(file);
            }
            catch (Exception)
            {
                return;
            }

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                int space = trimmed.IndexOf(' ');
                if (space <= 0) continue;

                string key = trimmed.Substring(0, space);
                string value = trimmed.Substring(space + 1).Trim();

                if (key == "height")
                {
                    long parsed;
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        scanned = parsed;

                    continue;
                }

                if (key == "server" && !Entries.Contains(value)) Entries.Add(value);
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "unreadable";

            string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();

            return flat.Length <= 60 ? flat : flat.Substring(0, 57) + "...";
        }
    }
}
