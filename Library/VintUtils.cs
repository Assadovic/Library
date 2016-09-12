using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Library
{
    public static class VintUtils
    {
        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[32]);

        public static void WriteVint(Stream stream, long value)
        {
            if (value < 0) value = 0;

            if (value <= 0x7F)
            {
                stream.WriteByte((byte)value);
            }
            else if (value <= 0x3FFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 2);
            }
            else if (value <= 0x1FFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 3);
            }
            else if (value <= 0xFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 4);
            }
            else if (value <= 0x7FFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 32 - 4) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[4] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 5);
            }
            else if (value <= 0x3FFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 40 - 5) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 32 - 4) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[4] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[5] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 6);
            }
            else if (value <= 0x1FFFFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 48 - 6) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 40 - 5) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 32 - 4) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[4] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[5] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[6] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 7);
            }
            else if (value <= 0xFFFFFFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 56 - 7) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 48 - 6) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 40 - 5) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 32 - 4) & 0x7F | 0x80);
                    buffer[4] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[5] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[6] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[7] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 8);
            }
            else if (value <= 0x7FFFFFFFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 64 - 8) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 56 - 7) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 48 - 6) & 0x7F | 0x80);
                    buffer[3] = (byte)((value >> 40 - 5) & 0x7F | 0x80);
                    buffer[4] = (byte)((value >> 32 - 4) & 0x7F | 0x80);
                    buffer[5] = (byte)((value >> 24 - 3) & 0x7F | 0x80);
                    buffer[6] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[7] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[8] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 9);
            }
        }

        public static long GetVint(Stream stream)
        {
            long result = 0;

            for (int count = 0; ; count++)
            {
                var b = stream.ReadByte();
                if (b < 0) return -1;

                result = (result << 7) | (byte)(b & 0x7F);
                if ((b & 0x80) != 0x80) break;

                if (count > 9) return -1;
            }

            return result;
        }
    }
}
