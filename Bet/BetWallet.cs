namespace Dragonator.Addons
{
    public class BetWallet : IServerWallet
    {
        public static bool Free { get; private set; }

        public string Name { get { return "bet"; } }

        public string Needs { get { return "Stealth daemon"; } }

        public bool Required { get { return !Free && BetAmountOption.BetXst > 0m; } }

        public void UseFree()
        {
            Free = true;

            string error;
            new BetAmountOption().TryApply("0", out error);
            new HostFeeOption().TryApply("", out error);
        }

        public bool Check(out string problem)
        {
            try
            {
                decimal balance = Stealth.Balance();
                decimal pot = BetAmountOption.BetXst * 2m;

                problem = balance < pot
                    ? "ok, but the balance is only " + Num.Xst(balance) + " XST and a full pot is " + Num.Xst(pot) + " XST"
                    : "ok, balance " + Num.Xst(balance) + " XST";

                return true;
            }
            catch (SwapRefused e)
            {
                problem = e.Message;
                return false;
            }
        }
    }
}
