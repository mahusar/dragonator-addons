using System.Globalization;

namespace Dragonator.Swapper
{
    public class MoneroSwapOption : IServerOption
    {
        public const decimal MinimumRate = 0.00000001m;

        public static bool Enabled { get; private set; }
        public static decimal XstPerXmr { get; private set; }

        public string Key { get { return "swap"; } }

        public string Label { get { return "swapper"; } }

        public string PromptText
        {
            get { return "XST paid per 1 XMR ('off' for no swapping)"; }
        }

        public string DescribeCurrent()
        {
            return Enabled ? Format(XstPerXmr) + " XST per XMR" : "no swapping";
        }

        public void ApplyDefault()
        {
            Enabled = false;
            XstPerXmr = 0m;
        }

        public bool TryApply(string input, out string error)
        {
            error = null;

            string cleaned = string.IsNullOrEmpty(input) ? "" : input.Trim();

            if (cleaned.Length == 0 ||
                cleaned.Equals("off", System.StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("none", System.StringComparison.OrdinalIgnoreCase))
            {
                ApplyDefault();
                return true;
            }

            decimal parsed;
            if (!decimal.TryParse(cleaned.Replace(',', '.'), NumberStyles.Number,
                                  CultureInfo.InvariantCulture, out parsed))
            {
                error = "'" + cleaned + "' is not a number. Use a rate, or 'off'.";
                return false;
            }

            if (parsed < MinimumRate)
            {
                error = "The rate must be at least " + Format(MinimumRate) + ", or 'off' for no swapping.";
                return false;
            }

            Enabled = true;
            XstPerXmr = parsed;
            return true;
        }

        public string ToWire()
        {
            return Enabled ? "swap=xmr@" + Format(XstPerXmr) : "swap=off";
        }

        private static string Format(decimal value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }
    }
}
