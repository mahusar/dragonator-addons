namespace Dragonator.Addons
{
    public static class BotsState
    {
        public const int DefaultPort = 6000;

        public const int Protocol = 1;

        public const string AuthTag = "dragonator-bot-auth-1";

        public const int KeyHexLength = 64;

        public const int MaxNameLength = 24;

        public const int MaxWaiting = 32;

        public static int ListenPort;

        public static int HandshakeTimeoutMs = 20000;

        public static int SocketTimeoutMs = 30000;

        public static int KeepAliveMs = 20000;

        public static bool Enabled
        {
            get { return ListenPort > 0; }
        }

        public static void Clear()
        {
            ListenPort = 0;
        }

        public static string Describe()
        {
            return Enabled ? "bots dial in on port " + ListenPort : "none";
        }

        public static string Challenge(string serverKeyHex, string nonceHex, string botKeyHex)
        {
            return AuthTag + "|" + serverKeyHex + "|" + nonceHex + "|" + botKeyHex;
        }

        public static bool TryReadPort(string text, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "Give the port bots should dial in on, for example 6000.";
                return false;
            }

            int parsed;
            if (!int.TryParse(text.Trim(), out parsed) || parsed < 1 || parsed > 65535)
            {
                error = "The port must be a number between 1 and 65535.";
                return false;
            }

            ListenPort = parsed;
            return true;
        }

        public static bool IsKey(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length != KeyHexLength) return false;

            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }

            return true;
        }

        public static string CleanName(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (char c in text)
            {
                if (char.IsControl(c) || c == '|') continue;
                sb.Append(c);

                if (sb.Length == MaxNameLength) break;
            }

            return sb.ToString().Trim();
        }

        public static string Short(string keyHex)
        {
            if (string.IsNullOrEmpty(keyHex)) return "";

            return keyHex.Length <= 16 ? keyHex : keyHex.Substring(0, 16);
        }
    }
}
