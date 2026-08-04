namespace Dragonator.Swapper
{
    public class ConfirmationsOption : IServerOption, IServerOptionListing
    {
        public const int Smallest = 1;
        public const int Largest = 60;
        public const int DefaultBlocks = 3;

        public static int Blocks { get; private set; }

        public int Order { get { return 50; } }

        public bool Ask { get { return SwapRate.Configured; } }

        public bool Show { get { return SwapRate.Configured; } }

        public string Key { get { return "swapconf"; } }

        public string Label { get { return "swap wait"; } }

        public string PromptText
        {
            get { return "XMR confirmations to wait for before sending XST"; }
        }

        public string DescribeCurrent()
        {
            return Blocks + (Blocks == 1 ? " confirmation" : " confirmations") +
                   " (about " + Blocks * 2 + " min)";
        }

        public void ApplyDefault()
        {
            Blocks = DefaultBlocks;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = Num.Clean(input);

            if (cleaned.Length == 0) return true;

            int parsed;
            if (!Num.TryInt(cleaned, out parsed))
            {
                error = Num.NotANumber(cleaned) + " Use a whole number of confirmations.";
                return false;
            }

            if (parsed < Smallest)
            {
                error = "Wait for at least " + Smallest +
                        " confirmation. Sending on an unconfirmed payment can be reversed.";
                return false;
            }

            if (parsed > Largest)
            {
                error = "The longest wait is " + Largest + " confirmations.";
                return false;
            }

            Blocks = parsed;
            return true;
        }

        public string ToWire()
        {
            return SwapRate.IsOffered ? "swapconf=" + Blocks : null;
        }
    }
}
