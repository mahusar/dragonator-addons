using System;
using System.Globalization;

namespace Dragonator.Addons
{
    internal static class Num
    {
        public static bool IsBlank(string input)
        {
            return string.IsNullOrEmpty(input) || input.Trim().Length == 0;
        }

        public static string Clean(string input)
        {
            return string.IsNullOrEmpty(input) ? "" : input.Trim();
        }

        public static bool IsOff(string cleaned)
        {
            return cleaned.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("no", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOn(string cleaned)
        {
            return cleaned.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                   cleaned.Equals("automatic", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryDecimal(string cleaned, out decimal parsed)
        {
            return decimal.TryParse(cleaned.Replace(',', '.').TrimEnd('%'), NumberStyles.Number,
                                    CultureInfo.InvariantCulture, out parsed);
        }

        public static bool TryInt(string cleaned, out int parsed)
        {
            return int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        public static string Xst(decimal value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        public static string Xmr(decimal value)
        {
            return value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        public static string Percent(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string NotANumber(string cleaned)
        {
            return "'" + cleaned + "' is not a number.";
        }
    }
}
