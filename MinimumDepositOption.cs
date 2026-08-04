namespace Dragonator.Swapper
{
    public class MinimumDepositOption : IServerOption, IServerOptionListing
    {
        public const decimal SmallestAllowed = 0.0001m;
        public const decimal DefaultXmr = 0.01m;

        public static decimal Xmr { get; private set; }

        public int Order { get { return 40; } }

        public bool Ask { get { return SwapRate.Configured; } }

        public bool Show { get { return SwapRate.Configured; } }

        public string Key { get { return "swapmin"; } }

        public string Label { get { return "swap minimum"; } }

        public string PromptText
        {
            get { return "smallest amount of XMR you will accept ('none' to take any)"; }
        }

        public string DescribeCurrent()
        {
            return Xmr <= 0m ? "any amount" : Num.Xmr(Xmr) + " XMR";
        }

        public void ApplyDefault()
        {
            Xmr = DefaultXmr;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0) return true;

            if (Num.IsOff(cleaned))
            {
                Xmr = 0m;
                return true;
            }

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned) + " Use an amount of XMR, or 'none'.";
                return false;
            }

            if (parsed < 0m)
            {
                error = "A minimum cannot be negative.";
                return false;
            }

            if (parsed > 0m && parsed < SmallestAllowed)
            {
                error = "Use at least " + Num.Xmr(SmallestAllowed) +
                        " XMR, or 'none' to take any amount.";
                return false;
            }

            Xmr = parsed;
            return true;
        }

        public string ToWire()
        {
            if (!SwapRate.IsOffered) return null;

            return Xmr <= 0m ? null : "swapmin=" + Num.Xmr(Xmr);
        }
    }
}
