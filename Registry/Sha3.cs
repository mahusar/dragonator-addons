using System;

namespace Dragonator.Addons
{
    internal static class Sha3
    {
        private const int Rate = 136;

        private static readonly ulong[] RoundConstants =
        {
            0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808aUL, 0x8000000080008000UL,
            0x000000000000808bUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
            0x000000000000008aUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000aUL,
            0x000000008000808bUL, 0x800000000000008bUL, 0x8000000000008089UL, 0x8000000000008003UL,
            0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800aUL, 0x800000008000000aUL,
            0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL
        };

        private static readonly int[] Rotations =
        {
            1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 2, 14,
            27, 41, 56, 8, 25, 43, 62, 18, 39, 61, 20, 44
        };

        private static readonly int[] Lanes =
        {
            10, 7, 11, 17, 18, 3, 5, 16, 8, 21, 24, 4,
            15, 23, 19, 13, 12, 2, 20, 14, 22, 9, 6, 1
        };

        public static byte[] Hash256(byte[] message)
        {
            if (message == null) message = new byte[0];

            ulong[] state = new ulong[25];

            int offset = 0;
            int remaining = message.Length;

            while (remaining >= Rate)
            {
                Absorb(state, message, offset);
                Permute(state);

                offset += Rate;
                remaining -= Rate;
            }

            byte[] tail = new byte[Rate];
            Array.Copy(message, offset, tail, 0, remaining);

            tail[remaining] = 0x06;
            tail[Rate - 1] |= 0x80;

            Absorb(state, tail, 0);
            Permute(state);

            byte[] output = new byte[32];
            for (int i = 0; i < output.Length; i++)
                output[i] = (byte)(state[i / 8] >> (8 * (i % 8)));

            return output;
        }

        private static void Absorb(ulong[] state, byte[] data, int offset)
        {
            for (int i = 0; i < Rate / 8; i++)
            {
                ulong lane = 0;
                for (int b = 0; b < 8; b++) lane |= (ulong)data[offset + i * 8 + b] << (8 * b);

                state[i] ^= lane;
            }
        }

        private static void Permute(ulong[] a)
        {
            ulong[] column = new ulong[5];
            ulong[] row = new ulong[5];

            for (int round = 0; round < 24; round++)
            {
                for (int x = 0; x < 5; x++)
                    column[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];

                for (int x = 0; x < 5; x++)
                {
                    ulong spread = column[(x + 4) % 5] ^ Rotate(column[(x + 1) % 5], 1);
                    for (int y = 0; y < 25; y += 5) a[y + x] ^= spread;
                }

                ulong carried = a[1];

                for (int i = 0; i < 24; i++)
                {
                    int lane = Lanes[i];
                    ulong held = a[lane];

                    a[lane] = Rotate(carried, Rotations[i]);
                    carried = held;
                }

                for (int y = 0; y < 25; y += 5)
                {
                    for (int x = 0; x < 5; x++) row[x] = a[y + x];
                    for (int x = 0; x < 5; x++)
                        a[y + x] = row[x] ^ (~row[(x + 1) % 5] & row[(x + 2) % 5]);
                }

                a[0] ^= RoundConstants[round];
            }
        }

        private static ulong Rotate(ulong value, int shift)
        {
            return (value << shift) | (value >> (64 - shift));
        }
    }
}
