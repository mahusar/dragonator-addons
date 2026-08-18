namespace Dragonator.Addons
{
    public class BotsOption : IServerOption, IServerOptionListing
    {
        public string Key { get { return "bot"; } }

        public string Label { get { return "bot arena"; } }

        public int Order { get { return 60; } }

        public bool Ask { get { return true; } }

        public bool Show { get { return true; } }

        public string PromptText
        {
            get
            {
                return "a port for bots to dial in on, " + BotsState.DefaultPort +
                       " is the usual one, or leave empty for a normal server humans play on";
            }
        }

        public string DescribeCurrent()
        {
            return BotsState.Describe();
        }

        public void ApplyDefault()
        {
            BotsState.Clear();
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            if (Num.IsBlank(input))
            {
                BotsState.Clear();
                return true;
            }

            string cleaned = Num.Clean(input);

            if (cleaned.ToLowerInvariant() == "none" || cleaned == "0")
            {
                BotsState.Clear();
                return true;
            }

            return BotsState.TryReadPort(cleaned, out error);
        }

        public string ToWire()
        {
            return BotsState.Enabled ? "bots=arena;botport=" + BotsState.ListenPort : "bots=0";
        }
    }
}
