using System;
using System.Collections.Generic;
using System.Threading;

namespace Dragonator.Swapper
{
    internal static class Watcher
    {
        public const int PollSeconds = 60;
        public const int RetrySeconds = 20;

        private static readonly object guard = new object();

        private static Thread worker;
        private static string problem;
        private static int waiting;

        public static string Problem
        {
            get
            {
                lock (guard) return problem;
            }
        }

        public static int Waiting
        {
            get
            {
                lock (guard) return waiting;
            }
        }

        public static void EnsureRunning()
        {
            lock (guard)
            {
                if (worker != null) return;

                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "swapper-watcher";
                worker.Start();
            }
        }

        private static void Loop()
        {
            while (true)
            {
                int wait = PollSeconds;

                try
                {
                    Sweep();
                }
                catch (SwapRefused e)
                {
                    lock (guard) problem = e.Message;
                    wait = RetrySeconds;
                }
                catch (Exception e)
                {
                    lock (guard) problem = "the deposit check failed (" + e.GetType().Name + ")";
                    wait = RetrySeconds;
                }

                try
                {
                    Thread.Sleep(wait * 1000);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        private static void Sweep()
        {
            if (!SwapRate.Configured) return;

            List<Incoming> arrivals = Monero.Arrivals();
            List<string> notes = new List<string>();
            int pending = 0;

            foreach (Incoming arrival in arrivals)
            {
                if (Ledger.Knows(arrival.Key)) continue;

                if (arrival.Confirmations < ConfirmationsOption.Blocks)
                {
                    pending++;
                    continue;
                }

                if (!Settle(arrival, notes)) pending++;
            }

            foreach (string entry in Ledger.Stuck()) notes.Add(entry);

            lock (guard)
            {
                waiting = pending;
                problem = notes.Count == 0 ? null : Summarise(notes);
            }
        }

        private static string Summarise(List<string> notes)
        {
            if (notes.Count <= 3) return string.Join("; ", notes.ToArray());

            string[] first = new string[3];
            notes.CopyTo(0, first, 0, 3);

            return string.Join("; ", first) + "; and " + (notes.Count - 3) + " more";
        }

        private static bool Settle(Incoming arrival, List<string> notes)
        {
            if (MinimumDepositOption.Xmr > 0m && arrival.Xmr < MinimumDepositOption.Xmr)
            {
                Ledger.Write(arrival.Key, Ledger.Held, arrival.Xmr, 0m,
                             "below the " + Num.Xmr(MinimumDepositOption.Xmr) + " XMR minimum");
                return true;
            }

            SwapRecord record = SwapStore.Find(arrival.Index);
            if (record == null)
            {
                Ledger.Write(arrival.Key, Ledger.Held, arrival.Xmr, 0m,
                             "no swap was recorded for subaddress " + arrival.Index);
                return true;
            }

            decimal owed = arrival.Xmr * record.Rate;

            if (owed <= 0m)
            {
                Ledger.Write(arrival.Key, Ledger.Held, arrival.Xmr, owed, "the locked rate works out to nothing");
                return true;
            }

            if (MaximumSwapOption.Exceeds(owed))
            {
                Ledger.Write(arrival.Key, Ledger.Held, arrival.Xmr, owed,
                             "over the " + Num.Xst(MaximumSwapOption.Xst) + " XST single-swap maximum");
                return true;
            }

            decimal balance = Stealth.Balance();

            if (ReserveOption.Breaches(balance, owed))
            {
                notes.Add("waiting on XST: " + Num.Xst(owed) + " owed, balance " + Num.Xst(balance) +
                          ", reserve " + Num.Xst(ReserveOption.Xst));

                return false;
            }

            Ledger.Write(arrival.Key, Ledger.Claimed, arrival.Xmr, owed, "sending to " + record.Payout);

            string txid = Stealth.Send(record.Payout, owed);

            Ledger.Write(arrival.Key, Ledger.Paid, arrival.Xmr, owed, txid);

            return true;
        }
    }
}
