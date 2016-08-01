using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Library.Utilities
{
    static class IntegerUtilities
    {
        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[16]);

        public static void WriteInt(Stream stream, int value)
        {
            if (value <= 0)
            {
                stream.WriteByte(0x00);
            }
            else if (value < 0x7F)
            {
                stream.WriteByte((byte)value);
            }
            else if (value < 0x3FFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 2);
            }
            else if (value < 0x1FFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 16 - 2) & 0x7F | 0x80);
                    buffer[1] = (byte)((value >> 8 - 1) & 0x7F | 0x80);
                    buffer[2] = (byte)((value >> 0 - 0) & 0x7F);
                }

                stream.Write(buffer, 0, 3);
            }
            else if (value < 0xFFFFFFF)
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
            else if (value <= 0x7FFFFFFF)
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
        }

        public static void WriteLong(Stream stream, long value)
        {
            if (value <= 0)
            {
                stream.WriteByte(0x00);
            }
            else if (value < 0x7FFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 8 * 3 - 0));
                    buffer[1] = (byte)((value >> 8 * 2 - 0));
                    buffer[2] = (byte)((value >> 8 * 1 - 0));
                    buffer[3] = (byte)((value >> 8 * 0 - 0));
                }

                stream.Write(buffer, 0, 4);
            }
            else if (value < 0x3FFFFFFFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 8 * 7 - 1) | 0x80);
                    buffer[1] = (byte)((value >> 8 * 6 - 1));
                    buffer[2] = (byte)((value >> 8 * 5 - 1));
                    buffer[3] = (byte)((value >> 8 * 4 - 1));

                    buffer[4] = (byte)((value >> 8 * 3 - 0) & 0x7F);
                    buffer[5] = (byte)((value >> 8 * 2 - 0));
                    buffer[6] = (byte)((value >> 8 * 1 - 0));
                    buffer[7] = (byte)((value >> 8 * 0 - 0));
                }

                stream.Write(buffer, 0, 8);
            }
            else if (value <= 0x7FFFFFFFFFFFFFFF)
            {
                var buffer = _threadLocalBuffer.Value;

                {
                    buffer[0] = (byte)((value >> 8 * 11 - 2) | 0x80);
                    buffer[1] = (byte)((value >> 8 * 10 - 2));
                    buffer[2] = (byte)((value >> 8 * 9 - 2));
                    buffer[3] = (byte)((value >> 8 * 8 - 2));

                    buffer[4] = (byte)((value >> 8 * 7 - 1) | 0x80);
                    buffer[5] = (byte)((value >> 8 * 6 - 1));
                    buffer[6] = (byte)((value >> 8 * 5 - 1));
                    buffer[7] = (byte)((value >> 8 * 4 - 1));

                    buffer[8] = (byte)((value >> 8 * 3 - 0) & 0x7F);
                    buffer[9] = (byte)((value >> 8 * 2 - 0));
                    buffer[10] = (byte)((value >> 8 * 1 - 0));
                    buffer[11] = (byte)((value >> 8 * 0 - 0));
                }

                stream.Write(buffer, 0, 12);
            }
        }

        public static int GetInt(Stream stream)
        {
            int result = 0;

            for (int count = 0; ; count++)
            {
                var b = stream.ReadByte();
                if (b < 0) return -1;

                result = (result << 7) | (byte)(b & 0x7F);
                if ((b & 0x80) != 0x80) break;

                if (count > 5) return -1;
            }

            return result;
        }

        public static long GetLong(Stream stream)
        {
            long result = 0;

            for (int count = 0; ; count++)
            {
                uint temp = 0;

                for (int i = 0; i < 4; i++)
                {
                    var b = stream.ReadByte();
                    if (b < 0) return -1;

                    temp = (temp << 8) | (byte)b;
                }

                result = (result << (8 * 4) - 1) | (temp & 0x7FFFFFFF);
                if ((temp & 0x80000000) != 0x80000000) break;

                if (count > 3) return -1;
            }

            return result;
        }
    }
}
