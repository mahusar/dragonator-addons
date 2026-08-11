using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dragonator.Addons
{
    internal class BetLedger
    {
        public const string MatchPlaying = "playing";
        public const string MatchSettled = "settled";

        private const string Pending = "pending";
        private const string Sent = "sent";
        private const string Failed = "failed";

        public class Entry
        {
            public string Type;
            public string RecordId;
            public string MatchId;
            public string Kind;
            public string State;
            public int ConnectionId;
            public string DepositAddress;
            public string PayoutAddress;
            public decimal Amount;
            public string Txid;
            public string Error;
        }

        private readonly string path;
        private readonly object guard = new object();

        private BetLedger(string path)
        {
            this.path = path;
        }

        public static BetLedger ForPort(int port)
        {
            return new BetLedger(Path.Combine(Paths.Data, "bets-" + port.ToString(CultureInfo.InvariantCulture) + ".jsonl"));
        }

        public List<Entry> ReadAll()
        {
            List<Entry> entries = new List<Entry>();

            string[] lines;
            lock (guard)
            {
                if (!File.Exists(path)) return entries;

                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch (Exception e)
                {
                    throw new SwapRefused("cannot read the bet ledger: " + e.Message);
                }
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.Trim().Length == 0) continue;

                Entry entry = Parse(line);
                if (entry != null) entries.Add(entry);
            }

            return entries;
        }

        private static string TextOrNull(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (string.Equals(raw, "null", StringComparison.Ordinal)) return null;

            return Json.TextOf(raw);
        }

        private static Entry Parse(string line)
        {
            string type = TextOrNull(Json.Field(line, "type"));
            if (string.IsNullOrEmpty(type)) return null;

            decimal amount;
            Json.TryDecimalOf(Json.Field(line, "amount"), out amount);

            long connectionId;
            Json.TryLongOf(Json.Field(line, "connectionId"), out connectionId);

            return new Entry
            {
                Type = type,
                RecordId = TextOrNull(Json.Field(line, "recordId")),
                MatchId = TextOrNull(Json.Field(line, "matchId")),
                Kind = TextOrNull(Json.Field(line, "kind")),
                State = TextOrNull(Json.Field(line, "state")),
                ConnectionId = (int)connectionId,
                DepositAddress = TextOrNull(Json.Field(line, "depositAddress")),
                PayoutAddress = TextOrNull(Json.Field(line, "payoutAddress")),
                Amount = amount,
                Txid = TextOrNull(Json.Field(line, "txid")),
                Error = TextOrNull(Json.Field(line, "error"))
            };
        }

        private void Append(Entry entry)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('{');

            Text(sb, "type", entry.Type);
            Text(sb, "recordId", entry.RecordId);
            Text(sb, "matchId", entry.MatchId);
            Text(sb, "kind", entry.Kind);
            Text(sb, "state", entry.State);

            if (sb.Length > 1) sb.Append(',');
            sb.Append("\"connectionId\":").Append(entry.ConnectionId.ToString(CultureInfo.InvariantCulture));

            Text(sb, "depositAddress", entry.DepositAddress);
            Text(sb, "payoutAddress", entry.PayoutAddress);

            sb.Append(",\"amount\":").Append(Num.Xst(entry.Amount));

            Text(sb, "txid", entry.Txid);
            Text(sb, "error", entry.Error);
            Text(sb, "utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

            sb.Append('}');

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString() + "\n");

            lock (guard)
            {
                using (FileStream file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
            }
        }

        private static void Text(StringBuilder sb, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            if (sb.Length > 1) sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"").Append(Escape(value)).Append('"');
        }

        private static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        public void RecordIssue(string matchId, int connectionId, string depositAddress, string payoutAddress)
        {
            Append(new Entry
            {
                Type = "issue",
                MatchId = matchId,
                ConnectionId = connectionId,
                DepositAddress = depositAddress,
                PayoutAddress = payoutAddress
            });
        }

        public void RecordFunded(string matchId, int connectionId, string depositAddress,
                                 string payoutAddress, decimal amount)
        {
            Append(new Entry
            {
                Type = "funded",
                MatchId = matchId,
                ConnectionId = connectionId,
                DepositAddress = depositAddress,
                PayoutAddress = payoutAddress,
                Amount = amount
            });
        }

        public void RecordMatchState(string matchId, string state)
        {
            Append(new Entry { Type = "matchstate", MatchId = matchId, State = state });
        }

        public string BeginSend(string matchId, string kind, int connectionId, string address, decimal amount)
        {
            string recordId = Guid.NewGuid().ToString("N");

            Append(new Entry
            {
                Type = "send",
                RecordId = recordId,
                MatchId = matchId,
                Kind = kind,
                State = Pending,
                ConnectionId = connectionId,
                PayoutAddress = address,
                Amount = amount
            });

            return recordId;
        }

        public void CompleteSend(string recordId, string txid)
        {
            Append(new Entry { Type = "send", RecordId = recordId, State = Sent, Txid = txid });
        }

        public void FailSend(string recordId, string error)
        {
            Append(new Entry { Type = "send", RecordId = recordId, State = Failed, Error = error });
        }

        public bool HasSettled(string matchId, string kind, int connectionId)
        {
            List<Entry> all = ReadAll();

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry e in all)
                if (e.Type == "send" && e.MatchId == matchId && e.Kind == kind &&
                    e.ConnectionId == connectionId && !string.IsNullOrEmpty(e.RecordId))
                    ids.Add(e.RecordId);

            if (ids.Count == 0) return false;

            foreach (Entry e in all)
                if (e.Type == "send" && e.State == Sent && ids.Contains(e.RecordId)) return true;

            return false;
        }

        public List<Entry> GetUnresolvedSends()
        {
            Dictionary<string, Entry> pending = new Dictionary<string, Entry>(StringComparer.Ordinal);
            HashSet<string> resolved = new HashSet<string>(StringComparer.Ordinal);

            foreach (Entry e in ReadAll())
            {
                if (e.Type != "send" || string.IsNullOrEmpty(e.RecordId)) continue;

                if (e.State == Pending) pending[e.RecordId] = e;
                else resolved.Add(e.RecordId);
            }

            List<Entry> unresolved = new List<Entry>();
            foreach (KeyValuePair<string, Entry> pair in pending)
                if (!resolved.Contains(pair.Key)) unresolved.Add(pair.Value);

            return unresolved;
        }

        public List<Entry> GetOrphanedFundings()
        {
            Dictionary<string, Entry> funded = new Dictionary<string, Entry>(StringComparer.Ordinal);
            Dictionary<string, string> matchState = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> settledRecordIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, Entry> sendsByRecord = new Dictionary<string, Entry>(StringComparer.Ordinal);

            foreach (Entry e in ReadAll())
            {
                switch (e.Type)
                {
                    case "funded":
                        funded[e.MatchId + "|" + e.ConnectionId.ToString(CultureInfo.InvariantCulture)] = e;
                        break;
                    case "matchstate":
                        matchState[e.MatchId] = e.State;
                        break;
                    case "send":
                        if (string.IsNullOrEmpty(e.RecordId)) break;
                        if (e.State == Sent) settledRecordIds.Add(e.RecordId);
                        else if (e.State == Pending) sendsByRecord[e.RecordId] = e;
                        break;
                }
            }

            HashSet<string> paidOutMatches = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> refundedPlayers = new HashSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Entry> pair in sendsByRecord)
            {
                if (!settledRecordIds.Contains(pair.Key)) continue;

                Entry send = pair.Value;
                if (send.Kind == DragonatorApi.KindPayout) paidOutMatches.Add(send.MatchId);
                else if (send.Kind == DragonatorApi.KindRefund)
                    refundedPlayers.Add(send.MatchId + "|" + send.ConnectionId.ToString(CultureInfo.InvariantCulture));
            }

            List<Entry> orphaned = new List<Entry>();

            foreach (KeyValuePair<string, Entry> pair in funded)
            {
                Entry f = pair.Value;

                string state;
                if (matchState.TryGetValue(f.MatchId, out state) && state == MatchSettled) continue;
                if (paidOutMatches.Contains(f.MatchId)) continue;
                if (refundedPlayers.Contains(pair.Key)) continue;

                orphaned.Add(f);
            }

            return orphaned;
        }
    }
}
