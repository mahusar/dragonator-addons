namespace Dragonator.Addons
{
    public class ReserveOption : IServerOption, IServerOptionListing
    {
        public static decimal Xst { get; private set; }

        public int Order { get { return 60; } }

        public bool Ask { get { return SwapRate.Configured; } }

        public bool Show { get { return SwapRate.Configured; } }

        public string Key { get { return "swapreserve"; } }

        public string Label { get { return "swap reserve"; } }

        public string PromptText
        {
            get { return "XST held back for match payouts, never swapped ('none' for no reserve)"; }
        }

        public string DescribeCurrent()
        {
            return Xst <= 0m ? "nothing held back" : Num.Xst(Xst) + " XST held back";
        }

        public void ApplyDefault()
        {
            Xst = 0m;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0) return true;

            if (Num.IsOff(cleaned))
            {
                Xst = 0m;
                return true;
            }

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned) + " Use an amount of XST, or 'none'.";
                return false;
            }

            if (parsed < 0m)
            {
                error = "A reserve cannot be negative.";
                return false;
            }

            Xst = parsed;
            return true;
        }

        public static bool Breaches(decimal balanceXst, decimal payingXst)
        {
            return balanceXst - payingXst < Xst;
        }

        public string ToWire()
        {
            return null;
        }
    }
}
