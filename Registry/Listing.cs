using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dragonator.Addons
{
    internal class Listing
    {
        public const string Magic = "58535444";
        public const int Version = 1;
        public const int RecordBytes = 40;
        public const int HexLength = RecordBytes * 2;

        private const int LabelLength = 56;
        private const int PubkeyBytes = 32;
        private const byte OnionVersion = 3;
        private const string ChecksumSalt = ".onion checksum";

        public string Onion;
        public int Port;
        public int Flags;

        public string Entry
        {
            get { return Onion + ":" + Port.ToString(CultureInfo.InvariantCulture); }
        }

        public static string Encode(string onion, int port, int flags)
        {
            byte[] pubkey = PubkeyOf(onion);
            if (pubkey == null) return null;
            if (port <= 0 || port > 65535) return null;

            StringBuilder sb = new StringBuilder(HexLength);
            sb.Append(Magic);
            sb.Append(Version.ToString("x2", CultureInfo.InvariantCulture));
            sb.Append(Base32.ToHex(pubkey));
            sb.Append(((port >> 8) & 255).ToString("x2", CultureInfo.InvariantCulture));
            sb.Append((port & 255).ToString("x2", CultureInfo.InvariantCulture));
            sb.Append((flags & 255).ToString("x2", CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        public static Listing Decode(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < HexLength) return null;

            byte[] record = Base32.FromHex(hex.Substring(0, HexLength));
            if (record == null) return null;

            if (record[0] != 0x58 || record[1] != 0x53 || record[2] != 0x54 || record[3] != 0x44) return null;
            if (record[4] != Version) return null;

            byte[] pubkey = new byte[PubkeyBytes];
            Array.Copy(record, 5, pubkey, 0, PubkeyBytes);

            int port = (record[37] << 8) | record[38];
            if (port <= 0 || port > 65535) return null;

            string onion = OnionOf(pubkey);
            if (onion == null) return null;

            return new Listing { Onion = onion, Port = port, Flags = record[39] };
        }

        public static List<Listing> ScanBlock(string json)
        {
            List<Listing> found = new List<Listing>();
            if (string.IsNullOrEmpty(json)) return found;

            int at = 0;

            while (true)
            {
                at = json.IndexOf(Magic, at, StringComparison.Ordinal);
                if (at < 0) break;

                if (at + HexLength <= json.Length)
                {
                    Listing listing = Decode(json.Substring(at, HexLength));

                    if (listing != null && !Holds(found, listing)) found.Add(listing);
                }

                at += Magic.Length;
            }

            return found;
        }

        private static bool Holds(List<Listing> found, Listing listing)
        {
            foreach (Listing held in found)
                if (string.Equals(held.Entry, listing.Entry, StringComparison.Ordinal)) return true;

            return false;
        }

        public static string OnionOf(byte[] pubkey)
        {
            if (pubkey == null || pubkey.Length != PubkeyBytes) return null;

            byte[] salt = Encoding.ASCII.GetBytes(ChecksumSalt);
            byte[] payload = new byte[salt.Length + PubkeyBytes + 1];

            Array.Copy(salt, 0, payload, 0, salt.Length);
            Array.Copy(pubkey, 0, payload, salt.Length, PubkeyBytes);
            payload[payload.Length - 1] = OnionVersion;

            byte[] digest = Sha3.Hash256(payload);

            byte[] address = new byte[PubkeyBytes + 3];
            Array.Copy(pubkey, 0, address, 0, PubkeyBytes);
            address[PubkeyBytes] = digest[0];
            address[PubkeyBytes + 1] = digest[1];
            address[PubkeyBytes + 2] = OnionVersion;

            return Base32.Encode(address) + ".onion";
        }

        public static byte[] PubkeyOf(string onion)
        {
            if (string.IsNullOrEmpty(onion)) return null;

            string label = onion.Trim().ToLowerInvariant();
            if (label.EndsWith(".onion", StringComparison.Ordinal))
                label = label.Substring(0, label.Length - 6);

            if (label.Length != LabelLength) return null;

            byte[] decoded = Base32.Decode(label);
            if (decoded == null || decoded.Length < PubkeyBytes) return null;

            byte[] pubkey = new byte[PubkeyBytes];
            Array.Copy(decoded, 0, pubkey, 0, PubkeyBytes);

            return pubkey;
        }
    }
}
