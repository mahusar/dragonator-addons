namespace Dragonator.Addons
{
    internal static class Composition
    {
        public static void Read(string receipt, out bool contested, out bool bot)
        {
            int seats = 0;
            bot = false;
            contested = false;

            if (string.IsNullOrEmpty(receipt)) return;

            foreach (string raw in receipt.Split('\n'))
            {
                string line = raw.Trim('\r');
                if (!line.StartsWith("player=")) continue;

                seats++;

                string[] parts = line.Substring(7).Split(':');
                if (parts.Length > 1 && parts[1] == "bot") bot = true;
            }

            contested = seats >= 2 && !bot;
        }
    }
}
