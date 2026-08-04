using System;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Dragonator.Swapper
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
                    direct = HasFlag(DirectFlag);
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

                        return Body(ReadAll(tls));
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

        private static string Body(string response)
        {
            int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split < 0) throw new IOException("the reply had no complete header");

            string head = response.Substring(0, split);
            string body = response.Substring(split + 4);

            int status = Status(head);
            if (status != 200) throw new IOException("the price source answered " + status);

            return Chunked(head) ? Dechunk(body) : body;
        }

        private static int Status(string head)
        {
            int first = head.IndexOf(' ');
            if (first < 0) throw new IOException("the reply had no status line");

            int second = head.IndexOf(' ', first + 1);
            string code = second < 0 ? head.Substring(first + 1) : head.Substring(first + 1, second - first - 1);

            int status;
            if (!int.TryParse(code.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
                throw new IOException("the reply had no status code");

            return status;
        }

        private static bool Chunked(string head)
        {
            return head.IndexOf("transfer-encoding: chunked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Dechunk(string body)
        {
            StringBuilder sb = new StringBuilder();
            int at = 0;

            while (at < body.Length)
            {
                int eol = body.IndexOf("\r\n", at, StringComparison.Ordinal);
                if (eol < 0) break;

                string header = body.Substring(at, eol - at);
                int semicolon = header.IndexOf(';');
                if (semicolon >= 0) header = header.Substring(0, semicolon);

                int size;
                if (!int.TryParse(header.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size))
                    throw new IOException("the reply was chunked but a chunk size was not readable");

                if (size == 0) break;

                int start = eol + 2;
                if (start + size > body.Length) throw new IOException("the reply ended inside a chunk");

                sb.Append(body, start, size);
                at = start + size + 2;
            }

            return sb.ToString();
        }

        private static bool HasFlag(string flag)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
            }

            return false;
        }
    }
}
