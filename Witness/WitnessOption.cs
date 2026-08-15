namespace Dragonator.Addons
{
    public class WitnessOption : IServerOption, IServerOptionListing
    {
        public string Key { get { return "witness"; } }

        public string Label { get { return "match receipts"; } }

        public int Order { get { return 50; } }

        public bool Ask { get { return true; } }

        public bool Show { get { return true; } }

        public string PromptText
        {
            get
            {
                return "anchor signed match receipts on the chain, so anyone can check a match really happened (y/n)";
            }
        }

        public string DescribeCurrent()
        {
            if (!WitnessState.Anchoring) return "kept on this server only, not anchored";

            return "anchored, up to " + WitnessState.BatchSize
                 + " per write or every " + (WitnessState.BatchSeconds / 60) + " min";
        }

        public void ApplyDefault()
        {
            WitnessState.Anchoring = false;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            if (Num.IsBlank(input)) return true;

            string cleaned = Num.Clean(input).ToLowerInvariant();

            if (cleaned == "y" || cleaned == "yes" || cleaned == "1" || cleaned == "on")
            {
                WitnessState.Anchoring = true;
                return true;
            }

            if (cleaned == "n" || cleaned == "no" || cleaned == "0" || cleaned == "off")
            {
                WitnessState.Anchoring = false;
                return true;
            }

            error = "Answer y or n.";
            return false;
        }

        public string ToWire()
        {
            return WitnessState.Anchoring ? "witness=1" : "witness=0";
        }
    }
}
