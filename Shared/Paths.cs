using System;
using System.IO;
using System.Reflection;

namespace Dragonator.Addons
{
    internal static class Paths
    {
        private const string DataFlag = "-addondata";
        private const string LegacyDataFlag = "-swapdata";
        private const string SwapsFile = "swaps.txt";

        private static string data;

        public static string Data
        {
            get
            {
                if (data == null) data = Resolve();

                return data;
            }
        }

        public static string Swaps
        {
            get { return Path.Combine(Data, SwapsFile); }
        }

        private static string Resolve()
        {
            string given = Args.Value(DataFlag);
            if (string.IsNullOrEmpty(given)) given = Args.Value(LegacyDataFlag);
            if (!string.IsNullOrEmpty(given)) return given;

            try
            {
                string here = Assembly.GetExecutingAssembly().Location;

                if (!string.IsNullOrEmpty(here))
                {
                    string addons = Path.GetDirectoryName(here);
                    string parent = string.IsNullOrEmpty(addons) ? null : Path.GetDirectoryName(addons);

                    if (!string.IsNullOrEmpty(parent)) return parent;
                }
            }
            catch (Exception)
            {
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
