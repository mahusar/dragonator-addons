using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Dragonator.Addons
{
    public class BotDesk
    {
        private readonly object gate = new object();
        private readonly List<BotLink> waiting = new List<BotLink>();
        private readonly List<BotLink> connected = new List<BotLink>();
        private readonly BotLink[] seats = new BotLink[2];

        private readonly IMatchBotHost host;

        private TcpListener listener;
        private Thread acceptor;
        private volatile bool running;

        public BotDesk(IMatchBotHost host)
        {
            this.host = host;
        }

        public int Waiting
        {
            get
            {
                lock (gate)
                {
                    int live = 0;

                    foreach (BotLink link in waiting)
                        if (link.Alive) live++;

                    return live;
                }
            }
        }

        public int Connected
        {
            get { lock (gate) return connected.Count; }
        }

        public void Start(int port)
        {
            if (running) return;

            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();

            running = true;

            acceptor = new Thread(Accept);
            acceptor.IsBackground = true;
            acceptor.Name = "dragonator-bot-desk";
            acceptor.Start();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;

            try { if (listener != null) listener.Stop(); } catch (Exception) { }

            List<BotLink> closing;

            lock (gate)
            {
                closing = new List<BotLink>(connected);

                connected.Clear();
                waiting.Clear();

                for (int seat = 0; seat < seats.Length; seat++) seats[seat] = null;
            }

            foreach (BotLink link in closing) link.Stop();

            Thread thread = acceptor;
            acceptor = null;

            if (thread != null && thread.IsAlive) thread.Join(1000);
        }

        public BotLink Seat(int seat)
        {
            if (seat < 0 || seat >= seats.Length) return null;

            lock (gate)
            {
                BotLink link = seats[seat];
                return link != null && link.Alive ? link : null;
            }
        }

        public bool Claim(int seat)
        {
            if (seat < 0 || seat >= seats.Length) return false;

            BotLink taken = null;

            lock (gate)
            {
                BotLink held = seats[seat];
                if (held != null && held.Alive) return true;

                seats[seat] = null;

                while (waiting.Count > 0)
                {
                    BotLink next = waiting[0];
                    waiting.RemoveAt(0);

                    if (!next.Alive) continue;
                    if (Held(next)) continue;

                    next.Seat();
                    seats[seat] = next;
                    taken = next;
                    break;
                }
            }

            if (taken == null) return false;

            taken.Notify("SEATED|" + (seat + 1));
            Log(taken.Describe() + " takes seat " + (seat + 1) + ".");

            return true;
        }

        public void Release(int seat, string result)
        {
            if (seat < 0 || seat >= seats.Length) return;

            BotLink link;

            lock (gate)
            {
                link = seats[seat];
                seats[seat] = null;
            }

            if (link == null) return;

            link.Unseat();
            link.Notify("FINISHED|" + (result ?? ""));

            if (!link.Alive)
            {
                Forget(link);
                return;
            }

            lock (gate)
            {
                if (!waiting.Contains(link)) waiting.Add(link);
            }

            Log(link.Describe() + " left seat " + (seat + 1) + " and is back in the queue.");
        }

        private bool Held(BotLink link)
        {
            foreach (BotLink held in seats)
                if (held == link) return true;

            return false;
        }

        private void Accept()
        {
            while (running)
            {
                TcpClient client;

                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception e)
                {
                    if (running) Fail("the bot desk stopped listening - " + e.Message);
                    return;
                }

                TcpClient accepted = client;

                Thread worker = new Thread(delegate() { Serve(accepted); });
                worker.IsBackground = true;
                worker.Name = "dragonator-bot";
                worker.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            StreamReader reader = null;
            StreamWriter writer = null;

            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = BotsState.HandshakeTimeoutMs;
                client.SendTimeout = BotsState.HandshakeTimeoutMs;

                NetworkStream stream = client.GetStream();

                reader = new StreamReader(stream, new UTF8Encoding(false));
                writer = new StreamWriter(stream, new UTF8Encoding(false));

                BotLink link = Handshake(client, reader, writer);

                if (link == null)
                {
                    Close(client, reader, writer);
                    return;
                }

                client.ReceiveTimeout = BotsState.SocketTimeoutMs;
                client.SendTimeout = BotsState.SocketTimeoutMs;

                link.Run();
                Forget(link);
            }
            catch (Exception e)
            {
                Fail("a bot connection ended badly - " + e.Message);
                Close(client, reader, writer);
            }
        }

        private BotLink Handshake(TcpClient client, StreamReader reader, StreamWriter writer)
        {
            string hello = reader.ReadLine();
            if (hello == null) return null;

            string[] bits = hello.Trim().Split('|');

            if (bits.Length < 3 || !string.Equals(bits[0], "HELLO", StringComparison.OrdinalIgnoreCase))
                return Deny(writer, "start with HELLO|" + BotsState.Protocol + "|<public key>|<name>");

            int protocol;
            if (!int.TryParse(bits[1], out protocol) || protocol != BotsState.Protocol)
                return Deny(writer, "this server speaks bot protocol " + BotsState.Protocol);

            string key = bits[2].Trim().ToLowerInvariant();

            if (!BotsState.IsKey(key))
                return Deny(writer, "the public key must be " + BotsState.KeyHexLength + " hex characters");

            string name = BotsState.CleanName(bits.Length > 3 ? bits[3] : "");
            if (name.Length == 0) name = "bot " + BotsState.Short(key);

            string refusal = null;

            lock (gate)
            {
                if (connected.Count >= BotsState.MaxWaiting) refusal = "the queue is full - try again later";
                else if (Known(key)) refusal = "that key is already connected to this server";
            }

            if (refusal != null) return Deny(writer, refusal);

            string server = host != null ? (host.ServerKey ?? "") : "";
            string nonce = Nonce();

            Write(writer, "CHALLENGE|" + server + "|" + nonce);

            string proof = reader.ReadLine();
            if (proof == null) return null;

            string[] parts = proof.Trim().Split('|');

            if (parts.Length < 2 || !string.Equals(parts[0], "PROOF", StringComparison.OrdinalIgnoreCase))
                return Deny(writer, "answer the challenge with PROOF|<signature>");

            string message = BotsState.Challenge(server, nonce, key);

            if (host == null || !host.BotVerify(key, message, parts[1].Trim()))
                return Deny(writer, "that signature does not prove the key");

            BotLink link = new BotLink(client, reader, writer, key, name, Log, Fail);

            int position = 0;

            lock (gate)
            {
                if (Known(key)) refusal = "that key is already connected to this server";
                else
                {
                    connected.Add(link);
                    waiting.Add(link);
                    position = waiting.Count;
                }
            }

            if (refusal != null) return Deny(writer, refusal);

            Write(writer, "WELCOME|" + position + "|" + name);
            Log(link.Describe() + " dialled in, " + position + " in the queue.");

            return link;
        }

        private bool Known(string key)
        {
            foreach (BotLink link in connected)
                if (link.Alive && link.Key == key) return true;

            return false;
        }

        private void Forget(BotLink link)
        {
            lock (gate)
            {
                connected.Remove(link);
                waiting.Remove(link);

                for (int seat = 0; seat < seats.Length; seat++)
                    if (seats[seat] == link) seats[seat] = null;
            }
        }

        private BotLink Deny(StreamWriter writer, string reason)
        {
            Write(writer, "DENIED|" + reason);
            Log("a bot was turned away - " + reason + ".");

            return null;
        }

        private static void Write(StreamWriter writer, string line)
        {
            try
            {
                writer.Write(line.Replace("\r", "").Replace("\n", ""));
                writer.Write("\n");
                writer.Flush();
            }
            catch (Exception)
            {
            }
        }

        private static void Close(TcpClient client, StreamReader reader, StreamWriter writer)
        {
            try { if (reader != null) reader.Dispose(); } catch (Exception) { }
            try { if (writer != null) writer.Dispose(); } catch (Exception) { }
            try { if (client != null) client.Close(); } catch (Exception) { }
        }

        private static string Nonce()
        {
            byte[] bytes = new byte[32];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);

            char[] chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";

            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }

        private void Log(string message)
        {
            if (host != null) host.BotLog(message);
        }

        private void Fail(string reason)
        {
            if (host != null) host.BotFailed(reason);
        }
    }
}
