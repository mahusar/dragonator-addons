namespace Dragonator.Addons
{
    public class BetAmountOption : IServerOption, IServerOptionListing
    {
        public const decimal Minimum = 0.01m;
        public const decimal DefaultBet = 0.1m;

        public static decimal BetXst { get; private set; }

        public string Key { get { return "bet"; } }

        public string Label { get { return "bet"; } }

        public int Order { get { return 10; } }

        public bool Ask { get { return !BetWallet.Free; } }

        public bool Show { get { return true; } }

        public string PromptText
        {
            get { return "bet in XST each player pays (minimum " + Num.Xst(Minimum) + ", 0 for a free table)"; }
        }

        public string DescribeCurrent()
        {
            if (BetXst <= 0m) return "free table, no bet";

            return Num.Xst(BetXst) + " XST bet";
        }

        public void ApplyDefault()
        {
            BetXst = DefaultBet;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            if (Num.IsBlank(input)) return true;

            string cleaned = Num.Clean(input);

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned);
                return false;
            }

            if (parsed < 0m)
            {
                error = "The bet cannot be negative.";
                return false;
            }

            if (parsed > 0m && parsed < Minimum)
            {
                error = "The smallest bet is " + Num.Xst(Minimum) + " XST. Enter 0 for a free table.";
                return false;
            }

            BetXst = parsed;
            return true;
        }

        public string ToWire()
        {
            return "bet=" + Num.Xst(BetXst);
        }
    }
}
