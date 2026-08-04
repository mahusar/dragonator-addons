using System;

namespace Dragonator.Swapper
{
    internal static class Prices
    {
        public const decimal MaxXmrDisagreementPercent = 3m;
        public const int MaxXmrQuoteAgeSeconds = 1800;
        public const int MaxXstQuietSeconds = 172800;

        private const string GeckoHost = "api.coingecko.com";
        private const string GeckoPath =
            "/api/v3/simple/price?ids=monero&vs_currencies=usd&include_last_updated_at=true";

        private const string KrakenHost = "api.kraken.com";
        private const string KrakenPath = "/0/public/Ticker?pair=XMRUSD";

        private const string NoFinexHost = "xapi.finexbit.com";
        private const string NoFinexPath = "/v1/market";

        public static decimal Market(out string detail)
        {
            decimal gecko = Gecko();
            decimal kraken = Kraken();

            decimal spread = Spread(gecko, kraken);
            if (spread > MaxXmrDisagreementPercent)
                throw new PriceRefused("the two XMR sources disagree by " + Num.Percent(spread) +
                                       "% (CoinGecko " + Num.Xst(gecko) + ", Kraken " + Num.Xst(kraken) + ")");

            decimal xmr = gecko < kraken ? gecko : kraken;
            decimal xst = NoFinex();

            detail = "XMR $" + Num.Xst(xmr) + " (" + Num.Percent(spread) + "% apart), XST $" + Num.Xmr(xst);

            return xmr / xst;
        }

        private static decimal Gecko()
        {
            string json = Fetch(GeckoHost, GeckoPath, "CoinGecko");

            int monero = Json.KeyAt(json, 0, "monero");
            if (monero < 0) throw new PriceRefused("CoinGecko did not return an XMR price");

            decimal usd;
            if (!Json.TryNumber(json, monero, "usd", out usd) || usd <= 0m)
                throw new PriceRefused("CoinGecko's XMR price was not readable");

            long updated;
            if (!Json.TryLong(json, monero, "last_updated_at", out updated))
                throw new PriceRefused("CoinGecko did not say when its XMR price was taken");

            long age = Now() - updated;
            if (age > MaxXmrQuoteAgeSeconds || age < -MaxXmrQuoteAgeSeconds)
                throw new PriceRefused("CoinGecko's XMR price is " + age / 60 + " minutes old");

            return usd;
        }

        private static decimal Kraken()
        {
            string json = Fetch(KrakenHost, KrakenPath, "Kraken");

            int result = Json.KeyAt(json, 0, "result");
            if (result < 0) throw new PriceRefused("Kraken did not return a result");

            decimal last;
            if (!Json.TryNumber(json, result, "c", out last) || last <= 0m)
                throw new PriceRefused("Kraken's XMR price was not readable");

            return last;
        }

        private static decimal NoFinex()
        {
            string json = Fetch(NoFinexHost, NoFinexPath, "NoFinex");

            int at = json.IndexOf("\"XST_USDT\"", StringComparison.Ordinal);
            if (at < 0) throw new PriceRefused("NoFinex is not listing XST_USDT");

            int start = Json.FlatObjectStart(json, at);
            int end = Json.FlatObjectEnd(json, at);
            if (start < 0 || end <= start) throw new PriceRefused("NoFinex's XST entry was not readable");

            string row = json.Substring(start, end - start + 1);

            if (!Json.TrueAt(row, 0, "active"))
                throw new PriceRefused("NoFinex has the XST market switched off");

            decimal price;
            if (!Json.TryNumber(row, 0, "price", out price) || price <= 0m)
                throw new PriceRefused("NoFinex's XST price was not readable");

            long traded;
            if (Json.TryLong(row, 0, "timestamp", out traded))
            {
                long quiet = Now() - traded;
                if (quiet > MaxXstQuietSeconds)
                    throw new PriceRefused("XST has not traded on NoFinex for " + quiet / 3600 + " hours");
            }

            return price;
        }

        private static string Fetch(string host, string path, string name)
        {
            try
            {
                return Https.Get(host, path);
            }
            catch (PriceRefused)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new PriceRefused("could not reach " + name + " " + Https.Route + " (" + Short(e.Message) + ")");
            }
        }

        private static decimal Spread(decimal one, decimal two)
        {
            decimal smaller = one < two ? one : two;
            if (smaller <= 0m) throw new PriceRefused("an XMR price came back as zero");

            decimal larger = one < two ? two : one;

            return (larger - smaller) / smaller * 100m;
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "no detail";

            string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= 70 ? flat : flat.Substring(0, 67) + "...";
        }
    }

    internal class PriceRefused : Exception
    {
        public PriceRefused(string message) : base(message)
        {
        }
    }
}
