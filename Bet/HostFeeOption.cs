namespace Dragonator.Addons
{
    public class HostFeeOption : IServerOption, IServerOptionListing
    {
        public const decimal Minimum = 0.01m;

        public static decimal FeeXst { get; private set; }

        public string Key { get { return "fee"; } }

        public string Label { get { return "host fee"; } }

        public int Order { get { return 20; } }

        public bool Ask { get { return BetAmountOption.BetXst > 0m; } }

        public bool Show { get { return BetAmountOption.BetXst > 0m; } }

        public string PromptText
        {
            get { return "host fee in XST per match (minimum " + Num.Xst(Minimum) + ", blank for none)"; }
        }

        public string DescribeCurrent()
        {
            if (FeeXst <= 0m) return "no host fee";

            decimal winnings = BetAmountOption.BetXst;
            string share = winnings > 0m
                ? "  (" + Num.Percent(decimal.Round(FeeXst / winnings * 100m, 2)) + "% of winnings)"
                : "";

            return Num.Xst(FeeXst) + " XST per match" + share;
        }

        public void ApplyDefault()
        {
            FeeXst = 0m;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            if (Num.IsBlank(input))
            {
                FeeXst = 0m;
                return true;
            }

            string cleaned = Num.Clean(input);

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned);
                return false;
            }

            if (parsed < 0m)
            {
                error = "The host fee cannot be negative.";
                return false;
            }

            if (parsed > 0m && parsed < Minimum)
            {
                error = "The smallest host fee is " + Num.Xst(Minimum) + " XST. Leave blank for no fee.";
                return false;
            }

            decimal bet = BetAmountOption.BetXst;

            if (parsed > 0m && bet <= 0m)
            {
                error = "This is a free table, so there are no winnings to take a fee from.";
                return false;
            }

            if (parsed >= bet && bet > 0m)
            {
                error = "A fee of " + Num.Xst(parsed) + " XST would leave the winner with no more than their own " +
                        Num.Xst(bet) + " XST bet back. Keep the fee below the bet.";
                return false;
            }

            FeeXst = parsed;
            return true;
        }

        public string ToWire()
        {
            return "fee=" + Num.Xst(FeeXst);
        }
    }
}
