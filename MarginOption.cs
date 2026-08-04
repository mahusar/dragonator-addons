namespace Dragonator.Swapper
{
    public class MarginOption : IServerOption, IServerOptionListing
    {
        public const decimal MaximumPercent = 50m;
        public const decimal DefaultPercent = 2m;

        public static decimal Percent { get; private set; }

        public int Order { get { return 30; } }

        public bool Ask { get { return AutoRateOption.Enabled; } }

        public bool Show { get { return AutoRateOption.Enabled; } }

        public string Key { get { return "swapmargin"; } }

        public string Label { get { return "swap margin"; } }

        public string PromptText
        {
            get { return "your margin as a percent, kept off the market rate"; }
        }

        public string DescribeCurrent()
        {
            return Percent <= 0m ? "no margin, at market" : Num.Percent(Percent) + "% below market";
        }

        public void ApplyDefault()
        {
            Percent = DefaultPercent;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0) return true;

            if (Num.IsOff(cleaned))
            {
                Percent = 0m;
                return true;
            }

            decimal parsed;
            if (!Num.TryDecimal(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned) + " Use a percent, such as 2.";
                return false;
            }

            if (parsed < 0m)
            {
                error = "A margin cannot be negative — that would pay above market.";
                return false;
            }

            if (parsed > MaximumPercent)
            {
                error = "The largest margin is " + Num.Percent(MaximumPercent) +
                        "%. Anything more will not find takers.";
                return false;
            }

            Percent = parsed;
            return true;
        }

        public string ToWire()
        {
            return null;
        }
    }
}
