using UnityEngine;

namespace Dragonator.Swapper
{
    public static class SwapperInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            ServerOptions.Register(new MoneroSwapOption());
        }
    }
}
