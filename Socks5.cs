using System;
using System.IO;
using System.Text;

namespace Dragonator.Swapper
{
    internal static class Socks5
    {
        public static void Connect(Stream stream, string host, int port)
        {
            byte[] greeting = { 0x05, 0x01, 0x00 };
            stream.Write(greeting, 0, greeting.Length);
            stream.Flush();

            byte[] chosen = Read(stream, 2);
            if (chosen[0] != 0x05) throw new IOException("the proxy on 127.0.0.1:9050 is not SOCKS5");
            if (chosen[1] != 0x00) throw new IOException("the Tor proxy wants an authentication method we do not offer");

            byte[] name = Encoding.ASCII.GetBytes(host);
            if (name.Length > 255) throw new IOException("host name too long for SOCKS5");

            byte[] request = new byte[7 + name.Length];
            request[0] = 0x05;
            request[1] = 0x01;
            request[2] = 0x00;
            request[3] = 0x03;
            request[4] = (byte)name.Length;
            Buffer.BlockCopy(name, 0, request, 5, name.Length);
            request[5 + name.Length] = (byte)(port >> 8);
            request[6 + name.Length] = (byte)(port & 0xff);

            stream.Write(request, 0, request.Length);
            stream.Flush();

            byte[] head = Read(stream, 4);
            if (head[1] != 0x00) throw new IOException("Tor could not reach " + host + " (" + Explain(head[1]) + ")");

            int bound;
            switch (head[3])
            {
                case 0x01:
                    bound = 4;
                    break;
                case 0x04:
                    bound = 16;
                    break;
                case 0x03:
                    bound = Read(stream, 1)[0];
                    break;
                default:
                    throw new IOException("the Tor proxy replied with an address type we do not understand");
            }

            Read(stream, bound + 2);
        }

        private static byte[] Read(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int filled = 0;

            while (filled < count)
            {
                int read = stream.Read(buffer, filled, count - filled);
                if (read <= 0) throw new IOException("the Tor proxy closed the connection early");

                filled += read;
            }

            return buffer;
        }

        private static string Explain(byte code)
        {
            switch (code)
            {
                case 0x01: return "general failure";
                case 0x02: return "not allowed by the proxy rules";
                case 0x03: return "network unreachable";
                case 0x04: return "host unreachable";
                case 0x05: return "connection refused";
                case 0x06: return "timed out";
                case 0x07: return "command not supported";
                case 0x08: return "address type not supported";
                default: return "code " + code;
            }
        }
    }
}
