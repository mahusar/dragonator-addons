using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dragonator.Addons
{
    public class BetEscrow : IMatchEscrow
    {
        private const int RequiredConfirmations = 3;
        private const int PollMinConfirmations = 0;
        private const double FundingTimeoutSeconds = 300d;
        private const double PollIntervalSeconds = 5d;

        private class Seat
        {
            public int ConnectionId;
            public string Name;
            public string PayoutAddress;
            public string DepositAddress;
            public decimal Received;
            public bool Issuing;
            public bool Funded;
            public bool RefundStarted;
            public decimal LedgerRecordedAmount;
            public string LastMessage;
        }

        private class Issue
        {
            public Seat Seat;
            public string PayoutAddress;
            public Task<string> Task;
        }

        private class Send
        {
            public string MatchId;
            public string Kind;
            public int ConnectionId;
            public string Address;
            public decimal Amount;
            public string RecordId;
            public Seat Notify;
            public Task<string> Task;
        }

        private readonly Dictionary<int, Seat> seats = new Dictionary<int, Seat>();
        private readonly List<Issue> issues = new List<Issue>();
        private readonly List<Send> sends = new List<Send>();

        private IMatchEscrowHost host;
        private BetLedger ledger;

        private string matchId = "";
        private bool fundingActive;
        private bool ready;
        private bool closed;

        private DateTime fundingDeadlineUtc;
        private DateTime nextPollUtc;
        private Task<List<Receipt>> pollTask;

        private static decimal Bet { get { return BetAmountOption.BetXst; } }

        public void Attach(IMatchEscrowHost host)
        {
            this.host = host;

            ledger = BetLedger.ForPort(host.ServerPort);

            if (Bet <= 0m)
            {
                host.Log("Bet is 0 - this is a free table. No stake is collected and no payout is ever sent.");
                return;
            }

            decimal balance = Stealth.Balance();
            host.Log("Wallet reachable, balance " + Num.Xst(balance) + " XST, stake " + Num.Xst(Bet) +
                     " XST per player, " + RequiredConfirmations + " confirmations required.");

            ReportUnresolvedSends();
            RefundOrphanedFundings();
        }

        public void BeginMatch(string matchId, int[] connectionIds, string[] playerNames)
        {
            this.matchId = matchId;

            seats.Clear();
            issues.Clear();
            ready = false;
            closed = false;

            for (int i = 0; i < connectionIds.Length; i++)
            {
                seats[connectionIds[i]] = new Seat
                {
                    ConnectionId = connectionIds[i],
                    Name = playerNames != null && i < playerNames.Length ? playerNames[i] : "Player " + (i + 1)
                };
            }

            if (Bet <= 0m)
            {
                fundingActive = false;
                ready = true;
                host.Log("Match " + matchId + ": free table, no stake collected.");
                host.EscrowReady(matchId);
                return;
            }

            fundingActive = true;
            fundingDeadlineUtc = DateTime.UtcNow.AddSeconds(FundingTimeoutSeconds);
            nextPollUtc = DateTime.UtcNow;

            host.SetFundingDeadline(FundingTimeoutSeconds);
            host.Log("Match " + matchId + ": funding open for " + FundingTimeoutSeconds + "s, stake " +
                     Num.Xst(Bet) + " XST per player.");

            foreach (Seat seat in seats.Values)
            {
                host.SetPlayerStatus(seat.ConnectionId, "Waiting for payout address...");
                host.PromptForPayoutAddress(seat.ConnectionId, Num.Xst(Bet), RequiredConfirmations);
            }
        }

        public void SubmitPayoutAddress(string matchId, int connectionId, string payoutAddress)
        {
            Seat seat;
            if (!seats.TryGetValue(connectionId, out seat))
            {
                host.Warn("Payout address from a connection that is not in match " + this.matchId + ".");
                return;
            }

            if (!fundingActive)
            {
                Notify(seat, false, "The funding window is closed.");
                return;
            }

            if (!string.IsNullOrEmpty(seat.DepositAddress) || seat.Issuing)
            {
                Notify(seat, false, "Your deposit address has already been issued.");
                return;
            }

            string cleaned = Num.Clean(payoutAddress);
            if (cleaned.Length == 0)
            {
                Notify(seat, false, "Enter the XST address you want your winnings sent to.");
                return;
            }

            if (!Stealth.LooksLikeAddress(cleaned))
            {
                Notify(seat, false, "That is not a valid XST address. Check it and try again.");
                return;
            }

            seat.Issuing = true;

            issues.Add(new Issue
            {
                Seat = seat,
                PayoutAddress = cleaned,
                Task = Task.Run(() => Stealth.IsValidAddress(cleaned) ? Stealth.NewAddress() : null)
            });
        }

        public void PlayerLeft(string matchId, int connectionId)
        {
            if (!seats.ContainsKey(connectionId)) return;
            if (!fundingActive) return;

            host.Warn(seats[connectionId].Name + " left during funding - cancelling match " + this.matchId + ".");
            Void(this.matchId, "Your opponent left before the match was funded.");
        }

        public void Settle(string matchId, int winnerConnectionId)
        {
            if (Bet <= 0m) return;

            if (closed)
            {
                host.Warn("Settle called for match " + matchId + " after it was already closed - ignoring.");
                return;
            }

            Seat winner;
            if (!seats.TryGetValue(winnerConnectionId, out winner))
            {
                host.Error("Settle: connection " + winnerConnectionId + " is not in match " + matchId +
                           " - no payout sent.");
                return;
            }

            closed = true;
            fundingActive = false;

            decimal pot = 0m;
            foreach (Seat seat in seats.Values) pot += seat.Received;

            if (pot <= 0m)
            {
                host.Error("Match " + matchId + ": pot is " + Num.Xst(pot) + " - nothing to pay out.");
                return;
            }

            decimal fee = HostFeeOption.FeeXst;
            if (fee < 0m) fee = 0m;

            if (fee >= pot)
            {
                host.Error("Match " + matchId + ": host fee " + Num.Xst(fee) + " XST is not less than the pot " +
                           Num.Xst(pot) + " XST - paying the full pot instead of shorting the winner.");
                fee = 0m;
            }

            decimal payout = pot - fee;

            if (fee > 0m)
                host.Log("Match " + matchId + ": pot " + Num.Xst(pot) + " XST, host fee " + Num.Xst(fee) + " XST retained.");

            host.Log("Match " + matchId + ": paying " + Num.Xst(payout) + " XST to " + winner.Name +
                     " (" + winner.PayoutAddress + ").");

            Dispatch(matchId, DragonatorApi.KindPayout, winner.ConnectionId, winner.PayoutAddress, payout, winner);
        }

        public void Void(string matchId, string reason)
        {
            if (Bet <= 0m) return;
            if (closed) return;

            closed = true;

            bool wasFunding = fundingActive;
            fundingActive = false;

            host.Warn("Match " + matchId + " voided: " + reason);

            foreach (Seat seat in seats.Values)
            {
                if (seat.Received <= 0m || seat.RefundStarted)
                {
                    if (!seat.RefundStarted)
                    {
                        host.SetPlayerStatus(seat.ConnectionId, "<color=#FF0000>Cancelled</color>");
                        Notify(seat, false, reason);
                    }
                    continue;
                }

                seat.RefundStarted = true;
                host.SetPlayerStatus(seat.ConnectionId, "<color=#FFAA00>Refunding...</color>");
                Notify(seat, false, reason + " Refunding " + Num.Xst(seat.Received) + " XST to " + seat.PayoutAddress + ".");

                host.Log("Refunding " + Num.Xst(seat.Received) + " XST to " + seat.Name + " (" + seat.PayoutAddress + ").");
                Dispatch(matchId, DragonatorApi.KindRefund, seat.ConnectionId, seat.PayoutAddress, seat.Received, seat);
            }

            host.EscrowVoided(matchId, reason);

            if (wasFunding) host.SetFundingDeadline(0d);
        }

        public void Tick()
        {
            if (host == null) return;

            DrainIssues();
            DrainSends();

            if (!fundingActive) return;

            DrainPoll();

            if (pollTask == null && DateTime.UtcNow >= nextPollUtc && AnyAwaitingDeposit())
            {
                nextPollUtc = DateTime.UtcNow.AddSeconds(PollIntervalSeconds);
                pollTask = Task.Run(() => Stealth.Received(PollMinConfirmations));
            }

            if (!ready && AllFunded())
            {
                ready = true;
                fundingActive = false;

                host.Log("Match " + matchId + ": both players funded - starting.");
                ledger.RecordMatchState(matchId, BetLedger.MatchPlaying);

                host.SetFundingDeadline(0d);
                host.EscrowReady(matchId);
                return;
            }

            if (DateTime.UtcNow >= fundingDeadlineUtc)
            {
                host.Warn("Match " + matchId + ": funding deadline reached.");
                Void(matchId, "Funding timed out.");
            }
        }

        public void Shutdown()
        {
            fundingActive = false;
        }

        private void DrainIssues()
        {
            for (int i = issues.Count - 1; i >= 0; i--)
            {
                Issue issue = issues[i];
                if (issue.Task == null || !issue.Task.IsCompleted) continue;

                issues.RemoveAt(i);
                issue.Seat.Issuing = false;
                issue.Seat.LastMessage = null;

                if (Faulted(issue.Task, "issuing a deposit address"))
                {
                    Notify(issue.Seat, false, "Could not reach the wallet daemon. Try again.");
                    continue;
                }

                string depositAddress = issue.Task.Result;

                if (depositAddress == null)
                {
                    Notify(issue.Seat, false, "That is not a valid XST address. Check it and try again.");
                    continue;
                }

                issue.Seat.PayoutAddress = issue.PayoutAddress;
                issue.Seat.DepositAddress = depositAddress;

                ledger.RecordIssue(matchId, issue.Seat.ConnectionId, depositAddress, issue.PayoutAddress);

                host.Log(issue.Seat.Name + ": deposit " + depositAddress + ", payout " + issue.PayoutAddress);

                host.SetPlayerStatus(issue.Seat.ConnectionId, "Waiting for payment...");
                host.ShowDepositAddress(issue.Seat.ConnectionId, depositAddress, Num.Xst(Bet));
            }
        }

        private void DrainPoll()
        {
            if (pollTask == null || !pollTask.IsCompleted) return;

            Task<List<Receipt>> finished = pollTask;
            pollTask = null;

            if (Faulted(finished, "polling for deposits"))
            {
                host.Warn("Deposit poll failed - retrying next tick.");
                return;
            }

            List<Receipt> rows = finished.Result;

            foreach (Seat seat in seats.Values)
            {
                if (string.IsNullOrEmpty(seat.DepositAddress) || seat.Funded) continue;

                decimal received = 0m;
                int confirmations = 0;

                foreach (Receipt row in rows)
                {
                    if (!string.Equals(row.Address, seat.DepositAddress, StringComparison.Ordinal)) continue;

                    received = row.Amount;
                    confirmations = row.Confirmations;
                    break;
                }

                seat.Received = received;

                if (received > 0m && received != seat.LedgerRecordedAmount)
                {
                    ledger.RecordFunded(matchId, seat.ConnectionId, seat.DepositAddress, seat.PayoutAddress, received);
                    seat.LedgerRecordedAmount = received;
                }

                if (received <= 0m)
                {
                    host.SetPlayerStatus(seat.ConnectionId, "Waiting for payment...");
                    continue;
                }

                if (received < Bet)
                {
                    host.SetPlayerStatus(seat.ConnectionId,
                        "<color=#FFAA00>Underpaid " + Num.Xst(received) + "/" + Num.Xst(Bet) + "</color>");
                    Notify(seat, false, "Received " + Num.Xst(received) + " XST but the stake is " +
                                        Num.Xst(Bet) + " XST. Send the difference to the same address.");
                    continue;
                }

                if (confirmations < RequiredConfirmations)
                {
                    host.SetPlayerStatus(seat.ConnectionId,
                        "<color=#FFAA00>Confirming " + confirmations + "/" + RequiredConfirmations + "</color>");
                    Notify(seat, true, "Payment seen - waiting for " + RequiredConfirmations + " confirmations (" +
                                       confirmations + "/" + RequiredConfirmations + ").");
                    continue;
                }

                seat.Funded = true;
                host.SetPlayerStatus(seat.ConnectionId, "<color=#00FF00>Paid</color>");
                Notify(seat, true, "Payment confirmed (" + Num.Xst(received) + " XST). Waiting for your opponent.");
                host.Log(seat.Name + " funded: " + Num.Xst(received) + " XST at " + seat.DepositAddress);
            }
        }

        private void DrainSends()
        {
            for (int i = sends.Count - 1; i >= 0; i--)
            {
                Send send = sends[i];
                if (send.Task == null || !send.Task.IsCompleted) continue;

                sends.RemoveAt(i);

                string failure = FailureOf(send.Task);

                if (failure == null)
                {
                    string txid = send.Task.Result;

                    ledger.CompleteSend(send.RecordId, txid);
                    host.Log(send.Kind + " sent: " + Num.Xst(send.Amount) + " XST -> " + send.Address + ", txid " + txid);

                    if (send.Kind == DragonatorApi.KindPayout)
                        ledger.RecordMatchState(send.MatchId, BetLedger.MatchSettled);

                    host.SettlementSent(send.ConnectionId, send.Kind, txid);
                    continue;
                }

                ledger.FailSend(send.RecordId, failure);
                host.Error(send.Kind + " FAILED: " + Num.Xst(send.Amount) + " XST -> " + send.Address + " (" + failure +
                           "). Ledger record " + send.RecordId + " needs manual review.");

                if (send.Notify != null)
                {
                    send.Notify.LastMessage = null;
                    Notify(send.Notify, false, send.Kind + " could not be sent. Keep this reference: " + send.RecordId);
                }
            }
        }

        private void Dispatch(string sendMatchId, string kind, int connectionId, string address,
                              decimal amount, Seat notify)
        {
            if (string.IsNullOrEmpty(address) || amount <= 0m)
            {
                host.Error(kind + " skipped for match " + sendMatchId + ": address='" + address + "', amount=" +
                           Num.Xst(amount) + ". MANUAL REVIEW REQUIRED.");
                return;
            }

            if (ledger.HasSettled(sendMatchId, kind, connectionId))
            {
                host.Warn(kind + " for match " + sendMatchId + " / conn " + connectionId +
                          " already settled - not sending again.");
                return;
            }

            string recordId = ledger.BeginSend(sendMatchId, kind, connectionId, address, amount);

            sends.Add(new Send
            {
                MatchId = sendMatchId,
                Kind = kind,
                ConnectionId = connectionId,
                Address = address,
                Amount = amount,
                RecordId = recordId,
                Notify = notify,
                Task = Task.Run(() => Stealth.Send(address, amount))
            });
        }

        private void ReportUnresolvedSends()
        {
            List<BetLedger.Entry> unresolved = ledger.GetUnresolvedSends();

            foreach (BetLedger.Entry e in unresolved)
            {
                host.Error("UNRESOLVED " + e.Kind + " from a previous run: " + Num.Xst(e.Amount) + " XST -> " +
                           e.PayoutAddress + " (match " + e.MatchId + ", record " + e.RecordId + "). " +
                           "Verify against the daemon with listtransactions before resending.");
            }

            if (unresolved.Count > 0)
                host.Error(unresolved.Count + " unresolved send(s) need manual review.");
        }

        private void RefundOrphanedFundings()
        {
            List<BetLedger.Entry> orphaned = ledger.GetOrphanedFundings();
            if (orphaned.Count == 0) return;

            host.Warn("Found " + orphaned.Count + " unsettled stake(s) from a previous run - refunding.");

            foreach (BetLedger.Entry f in orphaned)
            {
                host.Warn("Recovery refund: " + Num.Xst(f.Amount) + " XST -> " + f.PayoutAddress +
                          " (match " + f.MatchId + ", conn " + f.ConnectionId + ").");

                Dispatch(f.MatchId, DragonatorApi.KindRefund, f.ConnectionId, f.PayoutAddress, f.Amount, null);
            }
        }

        private void Notify(Seat seat, bool success, string message)
        {
            if (seat.LastMessage == message) return;
            seat.LastMessage = message;

            host.Message(seat.ConnectionId, success, message);
        }

        private bool AnyAwaitingDeposit()
        {
            foreach (Seat seat in seats.Values)
                if (!string.IsNullOrEmpty(seat.DepositAddress) && !seat.Funded) return true;

            return false;
        }

        private bool AllFunded()
        {
            if (seats.Count < 2) return false;

            foreach (Seat seat in seats.Values)
                if (!seat.Funded) return false;

            return true;
        }

        private bool Faulted(Task task, string what)
        {
            string failure = FailureOf(task);
            if (failure == null) return false;

            host.Warn(what + " failed: " + failure);
            return true;
        }

        private static string FailureOf(Task task)
        {
            if (task.IsCanceled) return "the wallet call was cancelled";

            if (task.IsFaulted)
            {
                Exception e = task.Exception;
                return e == null ? "unknown wallet failure" : e.GetBaseException().Message;
            }

            return null;
        }
    }
}
