using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Dragonator.Addons
{
    public class BotLink
    {
        private class Job
        {
            public int Token;
            public string Line;
            public bool Expects;
        }

        private readonly object gate = new object();
        private readonly Queue<Job> pending = new Queue<Job>();
        private readonly Dictionary<int, string> answers = new Dictionary<int, string>();
        private readonly HashSet<int> abandoned = new HashSet<int>();

        private readonly TcpClient client;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;

        private readonly Action<string> log;
        private readonly Action<string> failed;

        private readonly ManualResetEvent wake = new ManualResetEvent(false);

        private volatile bool stopping;
        private volatile bool alive = true;
        private volatile bool seated;

        public string Key { get; private set; }

        public string Name { get; private set; }

        public bool Alive
        {
            get { return alive && !stopping; }
        }

        public bool Seated
        {
            get { return seated; }
        }

        public BotLink(TcpClient client, StreamReader reader, StreamWriter writer,
                       string key, string name, Action<string> log, Action<string> failed)
        {
            this.client = client;
            this.reader = reader;
            this.writer = writer;
            this.log = log;
            this.failed = failed;

            Key = key;
            Name = name;
        }

        public void Seat()
        {
            seated = true;
        }

        public void Unseat()
        {
            seated = false;

            lock (gate)
            {
                pending.Clear();
                answers.Clear();
                abandoned.Clear();
            }
        }

        public void Request(int token, string state)
        {
            Enqueue(new Job { Token = token, Line = state, Expects = true });
        }

        public void RequestSignature(int token, string digestHex)
        {
            Enqueue(new Job { Token = token, Line = "SIGN|" + digestHex, Expects = true });
        }

        public void Notify(string line)
        {
            Enqueue(new Job { Line = line, Expects = false });
        }

        public string Poll(int token)
        {
            lock (gate)
            {
                string answer;
                if (!answers.TryGetValue(token, out answer)) return null;

                answers.Remove(token);
                return answer;
            }
        }

        public void Cancel(int token)
        {
            lock (gate)
            {
                answers.Remove(token);
                abandoned.Add(token);
            }
        }

        public void Stop()
        {
            stopping = true;
            wake.Set();

            Drop();
        }

        public void Run()
        {
            int idleMs = 0;

            while (!stopping && alive)
            {
                Job job = null;

                lock (gate)
                {
                    if (pending.Count > 0) job = pending.Dequeue();
                }

                if (job == null)
                {
                    wake.Reset();
                    wake.WaitOne(250);

                    if (stopping || !alive) break;

                    idleMs += 250;

                    if (!seated && idleMs >= BotsState.KeepAliveMs)
                    {
                        idleMs = 0;
                        if (!KeepAlive()) break;
                    }

                    continue;
                }

                idleMs = 0;

                if (!job.Expects)
                {
                    if (!Send(job.Line)) break;
                    continue;
                }

                string answer = Exchange(job.Line);
                if (answer == null) break;

                lock (gate)
                {
                    if (abandoned.Remove(job.Token)) continue;

                    answers[job.Token] = answer;
                }
            }

            Drop();
        }

        private void Enqueue(Job job)
        {
            if (!Alive) return;

            lock (gate) pending.Enqueue(job);

            wake.Set();
        }

        private string Exchange(string line)
        {
            if (!Send(line)) return null;

            try
            {
                string reply = reader.ReadLine();

                if (reply == null)
                {
                    Report("it closed the connection");
                    return null;
                }

                return reply.Trim();
            }
            catch (Exception e)
            {
                Report(e.Message);
                return null;
            }
        }

        private bool KeepAlive()
        {
            if (!Send("PING")) return false;

            try
            {
                string reply = reader.ReadLine();

                if (reply == null)
                {
                    Report("it closed the connection while waiting for a seat");
                    return false;
                }

                if (!string.Equals(reply.Trim(), "PONG", StringComparison.OrdinalIgnoreCase))
                {
                    Report("it answered \"" + reply.Trim() + "\" to PING instead of PONG");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Report(e.Message);
                return false;
            }
        }

        private bool Send(string line)
        {
            try
            {
                writer.Write(line.Replace("\r", "").Replace("\n", ""));
                writer.Write("\n");
                writer.Flush();

                return true;
            }
            catch (Exception e)
            {
                Report(e.Message);
                return false;
            }
        }

        private void Report(string reason)
        {
            if (!alive) return;

            if (failed != null)
                failed(Describe() + " could not answer - " + reason);
        }

        public string Describe()
        {
            return Name + " (" + BotsState.Short(Key) + ")";
        }

        private void Drop()
        {
            if (!alive) return;
            alive = false;

            try { if (reader != null) reader.Dispose(); } catch (Exception) { }
            try { if (writer != null) writer.Dispose(); } catch (Exception) { }
            try { if (client != null) client.Close(); } catch (Exception) { }

            if (log != null) log(Describe() + " disconnected.");
        }
    }
}
