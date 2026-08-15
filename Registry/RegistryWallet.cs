using System;
using System.Globalization;

namespace Dragonator.Addons
{
    public class RegistryWallet : IServerWallet
    {
        public static bool Free { get; private set; }

        public string Name { get { return "server list"; } }

        public string Needs { get { return "Stealth daemon"; } }

        public bool Required { get { return !Free && RegistryState.Enabled; } }

        public void UseFree()
        {
            Free = true;
            RegistryState.Enabled = false;
        }

        public bool Check(out string problem)
        {
            if (!RegistryState.Enabled)
            {
                problem = "off";
                return true;
            }

            try
            {
                long height = Chain.Height();
                long behind = height - RegistryState.StartHeight;

                problem = behind < 0
                    ? "ok, but the daemon is at block " + height.ToString(CultureInfo.InvariantCulture) +
                      ", before the first known listing - nothing will be found until it syncs"
                    : "ok, chain at block " + height.ToString(CultureInfo.InvariantCulture);

                ChainDirectory.Begin();
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }
    }
}
