namespace Dragonator.Addons
{
    internal static class Monero
    {
        public const string Host = "127.0.0.1";
        public const int DefaultPort = 18083;
        public const string Path = "/json_rpc";

        private const string PortFlag = "-swapmoneroport";

        public static int Port
        {
            get { return Args.Number(PortFlag, DefaultPort, 1, 65535); }
        }

        public static Deposit CreateAddress()
        {
            string body = "{\"jsonrpc\":\"2.0\",\"id\":\"swap\",\"method\":\"create_address\"," +
                          "\"params\":{\"account_index\":0}}";

            string json = Rpc.Post(Host, Port, Path, body, null, null);

            int result = Json.KeyAt(json, 0, "result");
            if (result < 0) throw new SwapRefused("the Monero wallet gave no answer");

            int at = Json.ValueAt(json, result, "address");
            if (at < 0 || json[at] != '"') throw new SwapRefused("the Monero wallet returned no address");

            int end = json.IndexOf('"', at + 1);
            if (end < 0) throw new SwapRefused("the Monero wallet returned no address");

            string address = json.Substring(at + 1, end - at - 1);
            if (address.Length == 0) throw new SwapRefused("the Monero wallet returned an empty address");

            long index;
            if (!Json.TryLong(json, result, "address_index", out index))
                throw new SwapRefused("the Monero wallet did not say which subaddress it made");

            return new Deposit(address, index);
        }

        public static System.Collections.Generic.List<Incoming> Arrivals()
        {
            string body = "{\"jsonrpc\":\"2.0\",\"id\":\"swap\",\"method\":\"get_transfers\"," +
                          "\"params\":{\"in\":true,\"pool\":true}}";

            string json = Rpc.Post(Host, Port, Path, body, null, null);

            string result = Json.Field(json, "result");
            if (string.IsNullOrEmpty(result)) throw new SwapRefused("the Monero wallet gave no answer");

            System.Collections.Generic.List<Incoming> arrivals =
                new System.Collections.Generic.List<Incoming>();

            Gather(Json.Items(result, "in"), arrivals);
            Gather(Json.Items(result, "pool"), arrivals);

            return arrivals;
        }

        private static void Gather(System.Collections.Generic.List<string> items,
                                   System.Collections.Generic.List<Incoming> into)
        {
            foreach (string item in items)
            {
                string txid = Json.TextOf(Json.Field(item, "txid"));
                if (string.IsNullOrEmpty(txid)) continue;

                long atomic;
                if (!Json.TryLongOf(Json.Field(item, "amount"), out atomic) || atomic <= 0L) continue;

                long confirmations;
                if (!Json.TryLongOf(Json.Field(item, "confirmations"), out confirmations)) confirmations = 0L;

                long index;
                if (!Json.TryLongOf(Json.Field(Json.Field(item, "subaddr_index") ?? "", "minor"), out index))
                    continue;

                into.Add(new Incoming(txid, atomic / 1000000000000m, confirmations, index));
            }
        }
    }

    internal class Incoming
    {
        public readonly string Txid;
        public readonly decimal Xmr;
        public readonly long Confirmations;
        public readonly long Index;

        public Incoming(string txid, decimal xmr, long confirmations, long index)
        {
            Txid = txid;
            Xmr = xmr;
            Confirmations = confirmations;
            Index = index;
        }

        public string Key
        {
            get { return Txid + ":" + Index; }
        }
    }

    internal class Deposit
    {
        public readonly string Address;
        public readonly long Index;

        public Deposit(string address, long index)
        {
            Address = address;
            Index = index;
        }
    }
}
