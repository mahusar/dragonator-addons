namespace Dragonator.Addons
{
    public class RegistryOption : IServerOption, IServerOptionListing
    {
        public string Key { get { return "registry"; } }

        public string Label { get { return "server list"; } }

        public int Order { get { return 40; } }

        public bool Ask { get { return !RegistryWallet.Free; } }

        public bool Show { get { return true; } }

        public string PromptText
        {
            get { return "read the public server list from the chain, so players who reach this server find the others (y/n)"; }
        }

        public string DescribeCurrent()
        {
            if (!RegistryState.Enabled) return "not published";

            return ChainDirectory.Describe();
        }

        public void ApplyDefault()
        {
            RegistryState.Enabled = false;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            if (Num.IsBlank(input)) return true;

            string cleaned = Num.Clean(input).ToLowerInvariant();

            if (cleaned == "y" || cleaned == "yes" || cleaned == "1" || cleaned == "on")
            {
                RegistryState.Enabled = true;
                return true;
            }

            if (cleaned == "n" || cleaned == "no" || cleaned == "0" || cleaned == "off")
            {
                RegistryState.Enabled = false;
                return true;
            }

            error = "Answer y or n.";
            return false;
        }

        public string ToWire()
        {
            return RegistryState.Enabled ? "registry=1" : "";
        }
    }
}
