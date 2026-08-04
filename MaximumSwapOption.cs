namespace Dragonator.Swapper
{
    public class MaximumSwapOption : IServerOption, IServerOptionListing
    {
        public static decimal Xst { get; private set; }

        public int Order { get { return 70; } }

        public bool Ask { get { return SwapRate.Configured; } }

        public bool Show { get { return SwapRate.Configured; } }

        public string Key { get { return "swapmax"; } }

        public string Label { get { return "swap maximum"; } }

        public string PromptText
        {
            get { return "most XST you will pay out in one swap ('none' for no cap)"; }
        }

        public string DescribeCurrent()
        {
            return Xst <= 0m ? "no cap" : "up to " + Num.Xst(Xst) + " XST";
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

            if (parsed <= 0m)
            {
                error = "A cap of zero would refuse every swap. Use 'none' for no cap.";
                return false;
            }

            Xst = parsed;
            return true;
        }

        public static bool Exceeds(decimal payingXst)
        {
            return Xst > 0m && payingXst > Xst;
        }

        public string ToWire()
        {
            if (!SwapRate.IsOffered) return null;

            return Xst <= 0m ? null : "swapmax=" + Num.Xst(Xst);
        }
    }
}
