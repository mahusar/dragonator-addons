using System;
using System.Collections.Generic;

namespace Dragonator.Addons
{
    internal static class ScriptScan
    {
        private const string AsmKey = "\"asm\"";
        private const string NullData = "OP_RETURN ";

        public static List<string> NullDataPushes(string json)
        {
            List<string> pushes = new List<string>();

            if (string.IsNullOrEmpty(json)) return pushes;

            int at = 0;

            while (true)
            {
                int key = json.IndexOf(AsmKey, at, StringComparison.Ordinal);
                if (key < 0) break;

                int colon = json.IndexOf(':', key + AsmKey.Length);
                if (colon < 0) break;

                int open = json.IndexOf('"', colon + 1);
                if (open < 0) break;

                int close = json.IndexOf('"', open + 1);
                if (close < 0) break;

                string script = json.Substring(open + 1, close - open - 1);
                at = close + 1;

                if (!script.StartsWith(NullData, StringComparison.Ordinal)) continue;

                foreach (string token in script.Substring(NullData.Length).Split(' '))
                    if (token.Length > 0) pushes.Add(token);
            }

            return pushes;
        }
    }
}
