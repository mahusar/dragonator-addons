using System;

namespace Dragonator.Addons
{
    public class SwapWallet : IServerWallet
    {
        public static bool Free { get; private set; }

        public string Name { get { return "swapper"; } }

        public string Needs { get { return "Stealth daemon and monero-wallet-rpc"; } }

        public bool Required { get { return !Free && SwapRate.Configured; } }

        public void UseFree()
        {
            Free = true;

            string error;
            new AutoRateOption().TryApply("off", out error);
            new MoneroSwapOption().TryApply("off", out error);
        }

        public bool Check(out string problem)
        {
            decimal balance;

            try
            {
                balance = Stealth.Balance();
            }
            catch (Exception e)
            {
                problem = "Stealth: " + e.Message;
                return false;
            }

            try
            {
                Monero.Arrivals();
            }
            catch (Exception e)
            {
                problem = "Stealth ok, but Monero: " + e.Message;
                return false;
            }

            problem = "ok, Stealth balance " + Num.Xst(balance) + " XST, Monero reachable";
            return true;
        }
    }
}
