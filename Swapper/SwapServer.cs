using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dragonator.Addons
{
    internal static class SwapServer
    {
        public const int DefaultPort = 5556;
        public const int NewPerMinute = 6;
        public const int InfoPerMinute = 30;
        public const int MaxRequestBytes = 512;

        private const string PortFlag = "-swapport";
        private const int TimeoutMs = 20000;

        private static readonly object guard = new object();
        private static readonly Bucket issuing = new Bucket(NewPerMinute);
        private static readonly Bucket asking = new Bucket(InfoPerMinute);

        private static TcpListener listener;
        private static Thread worker;
        private static string problem;

        public static int Port
        {
            get { return Args.Number(PortFlag, DefaultPort, 1, 65535); }
        }

        public static bool Listening
        {
            get
            {
                lock (guard) return listener != null;
            }
        }

        public static string Problem
        {
            get
            {
                lock (guard) return problem;
            }
        }

        public static void EnsureRunning()
        {
            lock (guard)
            {
                if (listener != null || worker != null) return;

                try
                {
                    listener = new TcpListener(IPAddress.Loopback, Port);
                    listener.Start();
                    problem = null;
                }
                catch (Exception e)
                {
                    listener = null;
                    problem = "the swap port " + Port + " will not open (" + Short(e.Message) + ")";
                    return;
                }

                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "swapper-desk";
                worker.Start();
            }
        }

        private static void Loop()
        {
            while (true)
            {
                TcpListener open;
                lock (guard) open = listener;

                if (open == null) return;

                try
                {
                    using (TcpClient client = open.AcceptTcpClient()) Serve(client);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Serve(TcpClient client)
        {
            client.SendTimeout = TimeoutMs;
            client.ReceiveTimeout = TimeoutMs;

            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = TimeoutMs;
                stream.WriteTimeout = TimeoutMs;

                string request = ReadLine(stream);
                if (request == null) return;

                string reply = Answer(request);

                byte[] bytes = Encoding.UTF8.GetBytes(reply + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        private static string Answer(string request)
        {
            string[] parts = request.Split('|');
            string name = parts[0].Trim();

            try
            {
                if (string.Equals(name, "SWAP_INFO", StringComparison.OrdinalIgnoreCase))
                    return asking.Take() ? SwapDesk.Info() : "ERROR|too many requests, try again shortly";

                if (string.Equals(name, "SWAP_NEW", StringComparison.OrdinalIgnoreCase))
                {
                    if (!issuing.Take()) return "ERROR|too many requests, try again shortly";

                    return SwapDesk.Issue(parts.Length > 1 ? parts[1] : null);
                }
            }
            catch (SwapRefused e)
            {
                return "ERROR|" + Short(e.Message);
            }
            catch (Exception e)
            {
                return "ERROR|the swap desk could not do that (" + e.GetType().Name + ")";
            }

            return "ERROR|unknown request";
        }

        private static string ReadLine(Stream stream)
        {
            using (MemoryStream sink = new MemoryStream())
            {
                while (sink.Length < MaxRequestBytes)
                {
                    int next = stream.ReadByte();

                    if (next < 0) break;
                    if (next == '\n') break;
                    if (next == '\r') continue;

                    sink.WriteByte((byte)next);
                }

                string line = Encoding.UTF8.GetString(sink.ToArray()).Trim('﻿', ' ', '\t');

                return line.Length == 0 ? null : line;
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "no detail";

            string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= 90 ? flat : flat.Substring(0, 87) + "...";
        }

        private class Bucket
        {
            private readonly double perMinute;
            private double tokens;
            private DateTime last;

            public Bucket(int perMinute)
            {
                this.perMinute = perMinute;
                tokens = perMinute;
                last = DateTime.UtcNow;
            }

            public bool Take()
            {
                lock (this)
                {
                    DateTime now = DateTime.UtcNow;
                    double elapsed = (now - last).TotalSeconds;

                    if (elapsed > 0d)
                    {
                        last = now;
                        tokens += elapsed * (perMinute / 60d);
                        if (tokens > perMinute) tokens = perMinute;
                    }

                    if (tokens < 1d) return false;

                    tokens -= 1d;
                    return true;
                }
            }
        }
    }
}
