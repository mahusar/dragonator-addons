using System;

namespace Dragonator.Swapper
{
    internal static class SwapDesk
    {
        public static string Info()
        {
            decimal rate;
            string reason;

            if (!SwapRate.TryEffective(out rate, out reason)) return "ERROR|" + reason;

            return "OK|" + Num.Xst(rate) +
                   "|" + Num.Xmr(MinimumDepositOption.Xmr) +
                   "|" + ConfirmationsOption.Blocks +
                   "|" + Num.Xst(MaximumSwapOption.Xst);
        }

        public static string Issue(string payoutAddress)
        {
            decimal rate;
            string reason;

            if (!SwapRate.TryEffective(out rate, out reason)) return "ERROR|" + reason;

            string address = Num.Clean(payoutAddress);

            if (address.Length == 0) return "ERROR|no XST address was given";

            if (!Stealth.LooksLikeAddress(address))
                return "ERROR|that does not look like an XST address";

            if (!Stealth.IsValidAddress(address))
                return "ERROR|the wallet says that XST address is not valid";

            Deposit deposit = Monero.CreateAddress();

            SwapStore.Add(new SwapRecord(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), deposit.Index,
                                         deposit.Address, address, rate, ConfirmationsOption.Blocks));

            return "OK|" + deposit.Address +
                   "|" + Num.Xst(rate) +
                   "|" + Num.Xmr(MinimumDepositOption.Xmr) +
                   "|" + ConfirmationsOption.Blocks;
        }
    }
}
