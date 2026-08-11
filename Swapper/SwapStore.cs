using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dragonator.Addons
{
    internal static class SwapStore
    {
        private static readonly object guard = new object();
        private static readonly List<SwapRecord> records = new List<SwapRecord>();

        private static bool loaded;

        public static int Count
        {
            get
            {
                lock (guard)
                {
                    Load();
                    return records.Count;
                }
            }
        }

        public static List<SwapRecord> All
        {
            get
            {
                lock (guard)
                {
                    Load();
                    return new List<SwapRecord>(records);
                }
            }
        }

        public static SwapRecord Find(long index)
        {
            lock (guard)
            {
                Load();

                for (int i = records.Count - 1; i >= 0; i--)
                    if (records[i].Index == index) return records[i];

                return null;
            }
        }

        public static void Add(SwapRecord record)
        {
            lock (guard)
            {
                Load();

                byte[] line = Encoding.UTF8.GetBytes(record.ToLine() + "\n");

                using (FileStream file = new FileStream(Paths.Swaps, FileMode.Append, FileAccess.Write,
                                                        FileShare.Read))
                {
                    file.Write(line, 0, line.Length);
                    file.Flush(true);
                }

                records.Add(record);
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;

            string file = Paths.Swaps;

            try
            {
                if (!File.Exists(file)) return;

                foreach (string line in File.ReadAllLines(file))
                {
                    SwapRecord record = SwapRecord.FromLine(line);
                    if (record != null) records.Add(record);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    internal class SwapRecord
    {
        public readonly long Made;
        public readonly long Index;
        public readonly string Deposit;
        public readonly string Payout;
        public readonly decimal Rate;
        public readonly int Confirmations;

        public SwapRecord(long made, long index, string deposit, string payout, decimal rate, int confirmations)
        {
            Made = made;
            Index = index;
            Deposit = deposit;
            Payout = payout;
            Rate = rate;
            Confirmations = confirmations;
        }

        public string ToLine()
        {
            return Made.ToString(CultureInfo.InvariantCulture) + "|" +
                   Index.ToString(CultureInfo.InvariantCulture) + "|" +
                   Deposit + "|" + Payout + "|" +
                   Rate.ToString(CultureInfo.InvariantCulture) + "|" +
                   Confirmations.ToString(CultureInfo.InvariantCulture);
        }

        public static SwapRecord FromLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            string[] parts = line.Split('|');
            if (parts.Length < 6) return null;

            long made;
            long index;
            decimal rate;
            int confirmations;

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out made)) return null;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return null;
            if (!decimal.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out rate)) return null;
            if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out confirmations))
                return null;

            return new SwapRecord(made, index, parts[2], parts[3], rate, confirmations);
        }
    }
}
