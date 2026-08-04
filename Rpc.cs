using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Dragonator.Swapper
{
    internal static class Rpc
    {
        private const int TimeoutMs = 30000;
        private const int MaxBodyBytes = 4 * 1024 * 1024;

        public static string Post(string host, int port, string path, string body, string user, string password)
        {
            using (TcpClient client = new TcpClient())
            {
                client.SendTimeout = TimeoutMs;
                client.ReceiveTimeout = TimeoutMs;
                client.Connect(host, port);

                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = TimeoutMs;
                    stream.WriteTimeout = TimeoutMs;

                    Send(stream, host, port, path, body, user, password);

                    return HttpReply.Body(ReadAll(stream));
                }
            }
        }

        private static void Send(Stream stream, string host, int port, string path, string body,
                                string user, string password)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);

            StringBuilder sb = new StringBuilder();
            sb.Append("POST ").Append(path).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(host).Append(':').Append(port).Append("\r\n");
            sb.Append("User-Agent: Dragonator-swapper\r\n");
            sb.Append("Content-Type: application/json\r\n");
            sb.Append("Content-Length: ").Append(payload.Length).Append("\r\n");

            if (!string.IsNullOrEmpty(user))
            {
                string pair = user + ":" + (password ?? "");
                sb.Append("Authorization: Basic ")
                  .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)))
                  .Append("\r\n");
            }

            sb.Append("Connection: close\r\n\r\n");

            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            stream.Write(payload, 0, payload.Length);
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

                    if (sink.Length + read > MaxBodyBytes) throw new IOException("the reply is far too large");

                    sink.Write(buffer, 0, read);
                }

                return Encoding.UTF8.GetString(sink.ToArray());
            }
        }
    }
}
