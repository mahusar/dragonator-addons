using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Dragonator.Addons
{
    internal static class Https
    {
        public const string TorHost = "127.0.0.1";
        public const int TorPort = 9050;

        private const string DirectFlag = "-swapdirect";
        private const int TimeoutMs = 30000;
        private const int MaxBodyBytes = 2 * 1024 * 1024;

        private static bool routeChecked;
        private static bool direct;

        public static bool Direct
        {
            get
            {
                if (!routeChecked)
                {
                    routeChecked = true;
                    direct = Args.Has(DirectFlag);
                }

                return direct;
            }
        }

        public static string Route
        {
            get { return Direct ? "direct" : "via Tor"; }
        }

        public static string Get(string host, string path)
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = TimeoutMs;
                client.ReceiveTimeout = TimeoutMs;

                if (Direct) client.Connect(host, 443);
                else client.Connect(TorHost, TorPort);

                using (NetworkStream raw = client.GetStream())
                {
                    raw.ReadTimeout = TimeoutMs;
                    raw.WriteTimeout = TimeoutMs;

                    if (!Direct) Socks5.Connect(raw, host, 443);

                    using (SslStream tls = new SslStream(raw, false))
                    {
                        tls.AuthenticateAsClient(host);
                        Send(tls, host, path);

                        return HttpReply.Body(ReadAll(tls));
                    }
                }
            }
        }

        private static void Send(Stream stream, string host, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(host).Append("\r\n");
            sb.Append("User-Agent: Dragonator-swapper\r\n");
            sb.Append("Accept: application/json\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string ReadAll(Stream stream)
        {
            using (MemoryStream sink = new MemoryStream())
            {
                byte[] buffer = new byte[8192];

                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    if (sink.Length + read > MaxBodyBytes) throw new IOException("the reply is far larger than a price list");

                    sink.Write(buffer, 0, read);
                }

                return Encoding.UTF8.GetString(sink.ToArray());
            }
        }

    }
}
