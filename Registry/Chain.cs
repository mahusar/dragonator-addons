using System;
using System.Globalization;

namespace Dragonator.Addons
{
    internal class ChainUnreadable : Exception
    {
        public ChainUnreadable(string message) : base(message)
        {
        }
    }

    internal static class Chain
    {
        public static long Height()
        {
            string json = Stealth.Command("getblockcount", "");

            long height;
            if (!Json.TryLongOf(Json.Field(json, "result"), out height))
                throw new ChainUnreadable("the Stealth daemon did not report a block height");

            return height;
        }

        public static string HashAt(long height)
        {
            string json = Stealth.Command("getblockhash", height.ToString(CultureInfo.InvariantCulture));

            string hash = Json.TextOf(Json.Field(json, "result"));
            if (string.IsNullOrEmpty(hash))
                throw new ChainUnreadable("the Stealth daemon did not return the hash of block " +
                                          height.ToString(CultureInfo.InvariantCulture));

            return hash;
        }

        public static string Block(string hash)
        {
            string json = Stealth.Command("getblock", "\"" + hash + "\",true");

            string block = Json.Field(json, "result");
            if (string.IsNullOrEmpty(block))
                throw new ChainUnreadable("the Stealth daemon did not return block " + hash);

            return block;
        }
    }
}
