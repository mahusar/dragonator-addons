namespace Dragonator.Addons
{
    public class AutoRateOption : IServerOption, IServerOptionListing
    {
        public static bool Enabled { get; private set; }

        public int Order { get { return 20; } }

        public bool Ask { get { return !SwapWallet.Free; } }

        public bool Show { get { return false; } }

        public string Key { get { return "swapauto"; } }

        public string Label { get { return "swap rate"; } }

        public string PromptText
        {
            get { return "track the market rate automatically ('on' overrides the rate above)"; }
        }

        public string DescribeCurrent()
        {
            return Enabled ? "from live prices" : "as typed above";
        }

        public void ApplyDefault()
        {
            Enabled = false;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0) return true;

            if (Num.IsOn(cleaned))
            {
                Enabled = true;
                Swapper.Start();
                RateFeed.EnsureRunning();
                return true;
            }

            if (Num.IsOff(cleaned))
            {
                Enabled = false;
                return true;
            }

            error = "Answer 'on' to follow the market, or 'off' to use the rate you typed.";
            return false;
        }

        public string ToWire()
        {
            return null;
        }
    }
}
