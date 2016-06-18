using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;

namespace <# 1 #>
{
    static class ItemUtilities
    {
        private static readonly BufferManager _bufferManager = BufferManager.Instance;
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));
        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[8]);
        private static readonly byte[] _vector;

        static ItemUtilities()
        {
            _vector = new byte[4];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_vector);
            }
        }

        public static int GetHashCode(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length == 0) return 0;

            return (BitConverter.ToInt32(Crc32_Castagnoli.ComputeHash(
                new ArraySegment<byte>[]
                {
                    new ArraySegment<byte>(_vector),
                    new ArraySegment<byte>(buffer),
                }), 0));
        }

        public static int GetHashCode(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return 0;

            return (BitConverter.ToInt32(Crc32_Castagnoli.ComputeHash(
                new ArraySegment<byte>[]
                {
                    new ArraySegment<byte>(_vector),
                    new ArraySegment<byte>(buffer, offset, count),
                }), 0));
        }

        public static void Write(Stream stream, byte type, Stream exportStream)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
            {
                int length;

                while ((length = exportStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                {
                    stream.Write(safeBuffer.Value, 0, length);
                }
            }
        }

        public static void Write(Stream stream, byte type, string value)
        {
            Encoding encoding = _threadLocalEncoding.Value;

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(encoding.GetMaxByteCount(value.Length)))
            {
                var length = encoding.GetBytes(value, 0, value.Length, safeBuffer.Value, 0);

                stream.WriteByte(type);
                stream.Write(NetworkConverter.GetBytes(length), 0, 4);
                stream.Write(safeBuffer.Value, 0, length);
            }
        }

        public static void Write(Stream stream, byte type, byte[] value)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)value.Length), 0, 4);
            stream.Write(value, 0, value.Length);
        }

        public static void Write(Stream stream, byte type, byte value)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)1), 0, 4);
            stream.WriteByte(value);
        }

        public static void Write(Stream stream, byte type, short value)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)2), 0, 4);
            stream.Write(NetworkConverter.GetBytes(value), 0, 2);
        }

        public static void Write(Stream stream, byte type, int value)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
            stream.Write(NetworkConverter.GetBytes(value), 0, 4);
        }

        public static void Write(Stream stream, byte type, long value)
        {
            stream.WriteByte(type);
            stream.Write(NetworkConverter.GetBytes((int)8), 0, 4);
            stream.Write(NetworkConverter.GetBytes(value), 0, 8);
        }

        public static Stream GetStream(out byte id, Stream stream)
        {
            id = 0;

            {
                byte[] idBuffer = new byte[1];
                if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return null;
                id = idBuffer[0];
            }

            int length;
            {
                byte[] lengthBuffer = new byte[4];
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return null;
                length = NetworkConverter.ToInt32(lengthBuffer);
            }

            return new RangeStream(stream, stream.Position, length, true);
        }

        public static byte[] GetByteArray(Stream stream)
        {
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public static string GetString(Stream stream)
        {
            Encoding encoding = _threadLocalEncoding.Value;

            var length = (int)stream.Length;

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(length))
            {
                stream.Read(safeBuffer.Value, 0, length);

                return encoding.GetString(safeBuffer.Value, 0, length);
            }
        }

        public static int GetByte(Stream stream)
        {
            if (stream.Length != 1) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;

            stream.Read(buffer, 0, 1);

            return buffer[0];
        }

        public static int GetShort(Stream stream)
        {
            if (stream.Length != 2) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;

            stream.Read(buffer, 0, 2);

            return NetworkConverter.ToInt16(buffer);
        }

        public static int GetInt(Stream stream)
        {
            if (stream.Length != 4) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;

            stream.Read(buffer, 0, 4);

            return NetworkConverter.ToInt32(buffer);
        }

        public static long GetLong(Stream stream)
        {
            if (stream.Length != 8) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;

            stream.Read(buffer, 0, 8);

            return NetworkConverter.ToInt64(buffer);
        }
    }
}
