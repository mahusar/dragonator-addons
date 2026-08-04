using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Dragonator.Swapper
{
    internal static class HttpReply
    {
        public static string Body(string response)
        {
            int split = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (split < 0) throw new IOException("the reply had no complete header");

            string head = response.Substring(0, split);
            string body = response.Substring(split + 4);

            int status = Status(head);
            if (status != 200) throw new IOException("answered " + status);

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
    }
}
