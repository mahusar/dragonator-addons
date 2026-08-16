using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Dragonator.Addons
{
    public class WitnessAddon : IMatchWitness
    {
        private IMatchWitnessHost host;

        private readonly List<string> pendingDigests = new List<string>();
        private readonly List<byte[]> pendingLeaves = new List<byte[]>();

        private DateTime lastFlush = DateTime.UtcNow;

        public void Attach(IMatchWitnessHost witnessHost)
        {
            host = witnessHost;
            Log("recording match receipts to " + ReceiptStore.Root);

            if (!WitnessState.Anchoring) return;

            foreach (string digest in ReceiptStore.Unanchored())
            {
                if (pendingDigests.Contains(digest)) continue;

                pendingDigests.Add(digest);
                pendingLeaves.Add(Merkle.FromHex(digest));
            }

            if (pendingDigests.Count > 0)
                Log("re-queued " + pendingDigests.Count + " receipt(s) left unanchored by an earlier run");
        }

        public void Record(string receipt, string signatures, bool fullySigned)
        {
            if (string.IsNullOrEmpty(receipt)) return;

            if (!fullySigned)
            {
                Log("a receipt arrived that is not signed by every player - ignored");
                return;
            }

            string digest = DigestOf(receipt);

            ReceiptStore.Save(digest, receipt, signatures);

            if (!WitnessState.Anchoring)
            {
                Log("stored receipt " + Short(digest) + " (anchoring is off)");
                return;
            }

            if (pendingDigests.Contains(digest)) return;

            pendingDigests.Add(digest);
            pendingLeaves.Add(Merkle.FromHex(digest));

            Log("queued receipt " + Short(digest) + " for anchoring (" + pendingDigests.Count + " waiting)");

            if (pendingDigests.Count >= WitnessState.BatchSize) Flush();
        }

        public string Lookup(string digest)
        {
            return ReceiptStore.Read(digest);
        }

        public void Tick()
        {
            if (pendingDigests.Count == 0) return;
            if (!WitnessState.Anchoring) return;

            if ((DateTime.UtcNow - lastFlush).TotalSeconds < WitnessState.BatchSeconds) return;

            Flush();
        }

        public void Shutdown()
        {
            if (pendingDigests.Count > 0) Flush();
        }

        private void Flush()
        {
            lastFlush = DateTime.UtcNow;

            if (pendingDigests.Count == 0) return;

            List<string> digests = new List<string>(pendingDigests);
            List<byte[]> leaves = new List<byte[]>(pendingLeaves);

            pendingDigests.Clear();
            pendingLeaves.Clear();

            try
            {
                byte[] root = Merkle.Root(leaves);
                string record = Anchor.Encode(root, leaves.Count, FlagsFor(digests));

                string address = Stealth.NewAddress();
                string txid = Stealth.Send(address, WitnessState.AnchorAmount, new List<string> { record });

                for (int i = 0; i < digests.Count; i++)
                    ReceiptStore.Anchored(digests[i], txid, Merkle.Path(leaves, i));

                Anchor written = Anchor.Decode(record);

                Log("anchored " + digests.Count + " receipt(s), merkle root " + Short(Merkle.Hex(root)) +
                    ", txid " + txid + (written != null ? " - " + written.Describe() : ""));
            }
            catch (Exception e)
            {
                foreach (string digest in digests) pendingDigests.Add(digest);
                foreach (byte[] leaf in leaves) pendingLeaves.Add(leaf);

                Failed("could not anchor " + digests.Count + " receipt(s) (" + e.GetType().Name + ": " + e.Message + ") - they stay queued");
            }
        }

        private static int FlagsFor(List<string> digests)
        {
            bool contested = digests.Count > 0;
            bool bot = false;

            foreach (string digest in digests)
            {
                bool one, hasBot;
                Composition.Read(ReceiptStore.Read(digest), out one, out hasBot);

                if (!one) contested = false;
                if (hasBot) bot = true;
            }

            int flags = 0;
            if (contested) flags |= Anchor.FlagContested;
            if (bot) flags |= Anchor.FlagBot;

            return flags;
        }

        public static string DigestOf(string receipt)
        {
            using (SHA256 sha = SHA256.Create())
                return Merkle.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(receipt)));
        }

        private static string Short(string hex)
        {
            return string.IsNullOrEmpty(hex) || hex.Length <= 16 ? hex : hex.Substring(0, 16);
        }

        private void Log(string message)
        {
            if (host != null) host.WitnessLog(message);
        }

        private void Failed(string reason)
        {
            if (host != null) host.WitnessFailed(reason);
        }
    }
}
