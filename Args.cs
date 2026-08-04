using System;

namespace Dragonator.Swapper
{
    internal static class Args
    {
        public static bool Has(string flag)
        {
            string[] args = All();

            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public static string Value(string flag)
        {
            string[] args = All();

            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1].Trim();

            return null;
        }

        public static int Number(string flag, int fallback, int smallest, int largest)
        {
            string given = Value(flag);
            if (string.IsNullOrEmpty(given)) return fallback;

            int parsed;
            if (!Num.TryInt(given, out parsed)) return fallback;

            return parsed < smallest || parsed > largest ? fallback : parsed;
        }

        private static string[] All()
        {
            try
            {
                return Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }
    }
}
