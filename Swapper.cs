namespace Dragonator.Swapper
{
    internal static class Swapper
    {
        public static void Start()
        {
            SwapServer.EnsureRunning();
            Watcher.EnsureRunning();
        }

        public static string Trouble()
        {
            string desk = SwapServer.Problem;
            if (!string.IsNullOrEmpty(desk)) return desk;

            string running = Watcher.Problem;
            if (!string.IsNullOrEmpty(running)) return running;

            int stuck = Ledger.Stuck().Count;

            return stuck == 0
                ? null
                : stuck + (stuck == 1 ? " swap needs" : " swaps need") + " attention in credits.txt";
        }
    }
}
