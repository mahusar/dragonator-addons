using System;
using System.Globalization;
using System.IO;

namespace Dragonator.Addons
{
    internal static class Stealth
    {
        public const int ShortestAddress = 26;
        public const int LongestAddress = 64;

        private const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        private const string ConfigFile = "rpc.conf";

        private static string host;
        private static int port;
        private static string path;
        private static string user;
        private static string password;
        private static bool read;

        public static bool LooksLikeAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            if (address.Length < ShortestAddress || address.Length > LongestAddress) return false;

            for (int i = 0; i < address.Length; i++)
                if (Base58.IndexOf(address[i]) < 0) return false;

            return true;
        }

        public static bool IsValidAddress(string address)
        {
            if (!LooksLikeAddress(address)) return false;

            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"swap\",\"method\":\"validateaddress\"," +
                               "\"params\":[\"" + address + "\"]}");

            string result = Json.Field(json, "result");
            if (string.IsNullOrEmpty(result)) throw new SwapRefused("the StealthCoin wallet gave no answer");

            return string.Equals(Json.Field(result, "isvalid"), "true", StringComparison.Ordinal);
        }

        public static decimal Balance()
        {
            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"swap\",\"method\":\"getbalance\",\"params\":[]}");

            decimal balance;
            if (!Json.TryDecimalOf(Json.Field(json, "result"), out balance))
                throw new SwapRefused("the StealthCoin wallet did not report a balance");

            return balance;
        }

        public static string Send(string address, decimal amount)
        {
            if (!LooksLikeAddress(address)) throw new SwapRefused("refusing to send to a malformed address");
            if (amount <= 0m) throw new SwapRefused("refusing to send a zero amount");

            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"swap\",\"method\":\"sendtoaddress\",\"params\":[\"" +
                               address + "\"," + amount.ToString("0.########", CultureInfo.InvariantCulture) + "]}");

            string txid = Json.TextOf(Json.Field(json, "result"));
            if (string.IsNullOrEmpty(txid))
                throw new SwapRefused("the StealthCoin wallet did not return a transaction id");

            return txid;
        }

        private static string Call(string body)
        {
            return Rpc.Post(host, port, path, body, user, password);
        }

        private static void Load()
        {
            if (read) return;

            string file = Path.Combine(Paths.Data, ConfigFile);

            string url = null;
            string foundUser = null;
            string foundPassword = null;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch (Exception)
            {
                throw new SwapRefused("the swapper cannot read " + ConfigFile);
            }

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                int split = trimmed.IndexOf('=');
                if (split <= 0) continue;

                string key = trimmed.Substring(0, split).Trim().ToLowerInvariant();
                string value = trimmed.Substring(split + 1).Trim();

                if (key == "rpcuser") foundUser = value;
                else if (key == "rpcpassword") foundPassword = value;
                else if (key == "rpcurl") url = value;
            }

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(foundUser) || string.IsNullOrEmpty(foundPassword))
                throw new SwapRefused(ConfigFile + " is missing the wallet user, password or url");

            Split(url);

            user = foundUser;
            password = foundPassword;
            read = true;
        }

        private static void Split(string url)
        {
            string rest = url;

            int scheme = rest.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) rest = rest.Substring(scheme + 3);

            int slash = rest.IndexOf('/');
            path = slash < 0 ? "/" : rest.Substring(slash);
            if (slash >= 0) rest = rest.Substring(0, slash);

            int colon = rest.LastIndexOf(':');
            if (colon < 0)
            {
                host = rest;
                port = 46502;
                return;
            }

            host = rest.Substring(0, colon);

            int parsed;
            if (!int.TryParse(rest.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                throw new SwapRefused("the wallet url in " + ConfigFile + " has no readable port");

            port = parsed;
        }
    }

    internal class SwapRefused : Exception
    {
        public SwapRefused(string message) : base(message)
        {
        }
    }
}
