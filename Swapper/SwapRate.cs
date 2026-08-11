using System;

namespace Dragonator.Addons
{
    public static class SwapRate
    {
        public const int MaxQuoteAgeSeconds = 600;

        private static readonly object guard = new object();

        private static decimal marketRate;
        private static DateTime quotedAt;
        private static string blocked = "no price has been fetched yet";

        public static bool IsOffered
        {
            get
            {
                decimal rate;
                string reason;
                return TryEffective(out rate, out reason);
            }
        }

        public static bool Configured
        {
            get
            {
                return AutoRateOption.Enabled || MoneroSwapOption.ManualRate >= MoneroSwapOption.MinimumRate;
            }
        }

        public static void Publish(decimal xstPerXmrAtMarket)
        {
            if (xstPerXmrAtMarket < MoneroSwapOption.MinimumRate)
            {
                Invalidate("the computed rate came out unusable");
                return;
            }

            lock (guard)
            {
                marketRate = xstPerXmrAtMarket;
                quotedAt = DateTime.UtcNow;
                blocked = null;
            }
        }

        public static void Invalidate(string reason)
        {
            lock (guard)
            {
                marketRate = 0m;
                quotedAt = default(DateTime);
                blocked = Num.IsBlank(reason) ? "the automatic rate is unavailable" : reason.Trim();
            }
        }

        public static bool TryEffective(out decimal rate, out string reason)
        {
            if (Configured && !SwapServer.Listening)
            {
                rate = 0m;
                reason = SwapServer.Problem ?? "the swap desk is not listening yet";
                return false;
            }

            return AutoRateOption.Enabled
                ? TryAutomatic(out rate, out reason)
                : TryManual(out rate, out reason);
        }

        public static string Describe()
        {
            decimal rate;
            string reason;

            if (!TryEffective(out rate, out reason))
                return Configured ? "no swapping (" + reason + ")" : "no swapping";

            string source = AutoRateOption.Enabled
                ? "automatic, " + Num.Percent(MarginOption.Percent) + "% margin, " + Https.Route
                : "fixed";

            return Num.Xst(rate) + " XST per XMR (" + source + ")";
        }

        private static bool TryManual(out decimal rate, out string reason)
        {
            rate = MoneroSwapOption.ManualRate;

            if (rate < MoneroSwapOption.MinimumRate)
            {
                rate = 0m;
                reason = "no rate is set";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryAutomatic(out decimal rate, out string reason)
        {
            decimal market;
            DateTime taken;
            string held;

            lock (guard)
            {
                market = marketRate;
                taken = quotedAt;
                held = blocked;
            }

            rate = 0m;

            if (held != null)
            {
                reason = held;
                return false;
            }

            double age = (DateTime.UtcNow - taken).TotalSeconds;
            if (age < 0d || age > MaxQuoteAgeSeconds)
            {
                reason = "the last price is too old to use";
                return false;
            }

            decimal keep = 1m - MarginOption.Percent / 100m;
            decimal marked = market * keep;

            if (marked < MoneroSwapOption.MinimumRate)
            {
                reason = "the margin leaves nothing to pay out";
                return false;
            }

            rate = marked;
            reason = null;
            return true;
        }
    }
}
