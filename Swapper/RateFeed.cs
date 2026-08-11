using System;
using System.Threading;

namespace Dragonator.Addons
{
    internal static class RateFeed
    {
        public const int RefreshSeconds = 120;
        public const int RetrySeconds = 30;
        public const decimal MaxJumpPercent = 25m;

        private static readonly object guard = new object();

        private static Thread worker;
        private static decimal lastGood;

        public static void EnsureRunning()
        {
            lock (guard)
            {
                if (worker != null) return;

                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "swapper-rates";
                worker.Start();
            }
        }

        private static void Loop()
        {
            while (true)
            {
                int wait = RefreshSeconds;

                if (AutoRateOption.Enabled)
                {
                    try
                    {
                        Refresh();
                    }
                    catch (PriceRefused e)
                    {
                        SwapRate.Invalidate(e.Message);
                        wait = RetrySeconds;
                    }
                    catch (Exception e)
                    {
                        SwapRate.Invalidate("the price check failed (" + e.GetType().Name + ")");
                        wait = RetrySeconds;
                    }
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

        private static void Refresh()
        {
            string detail;
            decimal market = Prices.Market(out detail);

            decimal previous;
            lock (guard) previous = lastGood;

            if (previous > 0m)
            {
                decimal jump = Math.Abs(market - previous) / previous * 100m;

                if (jump > MaxJumpPercent)
                    throw new PriceRefused("the rate moved " + Num.Percent(jump) + "% since the last good check (" +
                                           Num.Xst(previous) + " to " + Num.Xst(market) + ") — refusing this reading");
            }

            lock (guard) lastGood = market;

            SwapRate.Publish(market);
        }
    }
}
