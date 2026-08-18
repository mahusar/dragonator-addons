using System;

namespace Dragonator.Addons
{
    public class BotsAddon : IMatchBot
    {
        private IMatchBotHost host;
        private BotDesk desk;

        public string Name
        {
            get { return "bots"; }
        }

        public int Seats
        {
            get { return BotsState.Enabled ? 2 : 0; }
        }

        public int Waiting
        {
            get { return desk == null ? 0 : desk.Waiting; }
        }

        public void Attach(IMatchBotHost matchHost)
        {
            host = matchHost;

            if (!BotsState.Enabled)
            {
                Log("no dial-in port is configured - no bot seat is offered.");
                return;
            }

            if (string.IsNullOrEmpty(host != null ? host.ServerKey : null))
            {
                Log("this server has no identity key, so bots cannot check who they are playing on.");
            }

            desk = new BotDesk(host);

            try
            {
                desk.Start(BotsState.ListenPort);
            }
            catch (Exception e)
            {
                desk = null;

                throw new InvalidOperationException(
                    "the bot desk could not listen on port " + BotsState.ListenPort +
                    " (" + e.GetType().Name + ": " + e.Message + ")", e);
            }

            Log("bots dial in on 127.0.0.1:" + BotsState.ListenPort + ".");
        }

        public bool SeatBot(int seat)
        {
            if (desk == null) return false;

            return desk.Claim(seat);
        }

        public string SeatName(int seat)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);

            return link == null ? "bot " + (seat + 1) : link.Name;
        }

        public string SeatKey(int seat)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);

            return link == null ? "" : link.Key;
        }

        public void Request(int seat, int token, string state)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);
            if (link == null) return;

            link.Request(token, state);
        }

        public void RequestSignature(int seat, int token, string digestHex)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);
            if (link == null) return;

            link.RequestSignature(token, digestHex);
        }

        public string Poll(int seat, int token)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);

            return link == null ? null : link.Poll(token);
        }

        public void Cancel(int seat, int token)
        {
            BotLink link = desk == null ? null : desk.Seat(seat);
            if (link == null) return;

            link.Cancel(token);
        }

        public void MatchEnded(int seat, string result)
        {
            if (desk == null) return;

            desk.Release(seat, result);
        }

        public void Shutdown()
        {
            if (desk == null) return;

            desk.Stop();
            desk = null;
        }

        private void Log(string message)
        {
            if (host != null) host.BotLog(message);
        }
    }
}
