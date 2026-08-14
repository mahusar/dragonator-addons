using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dragonator.Addons
{
    internal class Receipt
    {
        public readonly string Address;
        public readonly decimal Amount;
        public readonly int Confirmations;

        public Receipt(string address, decimal amount, int confirmations)
        {
            Address = address;
            Amount = amount;
            Confirmations = confirmations;
        }
    }

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
            if (string.IsNullOrEmpty(result)) throw new SwapRefused("the Stealth wallet gave no answer");

            return string.Equals(Json.Field(result, "isvalid"), "true", StringComparison.Ordinal);
        }

        public static string NewAddress()
        {
            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"dragonator\",\"method\":\"getnewaddress\",\"params\":[]}");
            Refuse(json, "getnewaddress");

            string address = Json.TextOf(Json.Field(json, "result"));
            if (string.IsNullOrEmpty(address))
                throw new SwapRefused("the Stealth wallet did not return a new address");

            return address;
        }

        public static List<Receipt> Received(int minConfirmations)
        {
            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"dragonator\",\"method\":\"listreceivedbyaddress\"," +
                               "\"params\":[" + minConfirmations.ToString(CultureInfo.InvariantCulture) + ",true]}");
            Refuse(json, "listreceivedbyaddress");

            List<Receipt> rows = new List<Receipt>();

            foreach (string item in Json.Items(json, "result"))
            {
                string address = Json.TextOf(Json.Field(item, "address"));
                if (string.IsNullOrEmpty(address)) continue;

                decimal amount;
                Json.TryDecimalOf(Json.Field(item, "amount"), out amount);

                long confirmations;
                Json.TryLongOf(Json.Field(item, "confirmations"), out confirmations);

                rows.Add(new Receipt(address, amount, confirmations < 0 ? 0 : (int)confirmations));
            }

            return rows;
        }

        private static void Refuse(string json, string method)
        {
            if (string.IsNullOrEmpty(json))
                throw new SwapRefused("the Stealth wallet gave no answer to " + method);

            string error = Json.Field(json, "error");
            if (!string.IsNullOrEmpty(error) && !string.Equals(error, "null", StringComparison.Ordinal))
                throw new SwapRefused("the Stealth wallet refused " + method + ": " + error);
        }

        public static decimal Balance()
        {
            Load();

            string json = Call("{\"jsonrpc\":\"1.0\",\"id\":\"swap\",\"method\":\"getbalance\",\"params\":[]}");

            decimal balance;
            if (!Json.TryDecimalOf(Json.Field(json, "result"), out balance))
                throw new SwapRefused("the Stealth wallet did not report a balance");

            return balance;
        }

        public const int MaxDataBytes = 48;

        public static string Send(string address, decimal amount)
        {
            return Send(address, amount, null);
        }

        public static string Send(string address, decimal amount, List<string> hexData)
        {
            if (!LooksLikeAddress(address)) throw new SwapRefused("refusing to send to a malformed address");
            if (amount <= 0m) throw new SwapRefused("refusing to send a zero amount");

            Load();

            StringBuilder body = new StringBuilder();
            body.Append("{\"jsonrpc\":\"1.0\",\"id\":\"dragonator\",\"method\":\"sendtoaddress\",\"params\":[\"");
            body.Append(address).Append("\",");
            body.Append(amount.ToString("0.########", CultureInfo.InvariantCulture));
            body.Append(",\"\",\"\",true");

            if (hexData != null && hexData.Count > 0)
            {
                body.Append(",[");

                for (int i = 0; i < hexData.Count; i++)
                {
                    string item = hexData[i];
                    if (string.IsNullOrEmpty(item) || item.Length % 2 != 0)
                        throw new SwapRefused("OP_RETURN data must be an even number of hex characters");

                    if (item.Length > MaxDataBytes * 2)
                        throw new SwapRefused("one OP_RETURN holds at most " + MaxDataBytes + " bytes");

                    if (i > 0) body.Append(',');
                    body.Append('"').Append(item).Append('"');
                }

                body.Append(']');
            }

            body.Append("]}");

            string json = Call(body.ToString());

            string txid = Json.TextOf(Json.Field(json, "result"));
            if (string.IsNullOrEmpty(txid))
                throw new SwapRefused("the Stealth wallet did not return a transaction id");

            return txid;
        }

        public static string Command(string method, string parameters)
        {
            Load();

            return Call("{\"jsonrpc\":\"1.0\",\"id\":\"dragonator\",\"method\":\"" + method +
                        "\",\"params\":[" + parameters + "]}");
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
                throw new SwapRefused("cannot read " + ConfigFile);
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
