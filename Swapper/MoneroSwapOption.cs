namespace Dragonator.Addons
{
    public class MoneroSwapOption : IServerOption, IServerOptionListing
    {
        public const decimal MinimumRate = 0.00000001m;

        public static decimal ManualRate { get; private set; }

        public int Order { get { return 10; } }

        public bool Ask { get { return !SwapWallet.Free; } }

        public bool Show { get { return true; } }

        public string Key { get { return "swap"; } }

        public string Label { get { return "swapper"; } }

        public string PromptText
        {
            get { return "XST paid per 1 XMR ('off' for no swapping)"; }
        }

        public string DescribeCurrent()
        {
            string trouble = Swapper.Trouble();

            return string.IsNullOrEmpty(trouble)
                ? SwapRate.Describe()
                : SwapRate.Describe() + "  —  " + trouble;
        }

        public void ApplyDefault()
        {
            ManualRate = 0m;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0 || Num.IsOff(cleaned))
            {
                ApplyDefault();
                return true;
            }

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned) + " Use a rate, or 'off'.";
                return false;
            }

            if (parsed < MinimumRate)
            {
                error = "The rate must be at least " + Num.Xst(MinimumRate) +
                        ", or 'off' for no swapping.";
                return false;
            }

            ManualRate = parsed;
            Swapper.Start();
            return true;
        }

        public string ToWire()
        {
            decimal rate;
            string reason;

            if (!SwapRate.TryEffective(out rate, out reason)) return "swap=off";

            return "swap=xmr@" + Num.Xst(rate) + ";swapport=" + SwapServer.Port;
        }
    }
}
