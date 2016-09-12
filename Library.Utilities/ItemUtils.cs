using System;
using System.Security.Cryptography;
using Library.Security;

namespace Library.Utilities
{
    static class ItemUtils
    {
        private static readonly byte[] _vector;

        static ItemUtils()
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
    }
}
