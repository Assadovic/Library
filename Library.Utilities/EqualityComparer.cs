using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Library;

namespace Library.Utilities
{
    class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y)
        {
            if ((x == null) != (y == null)) return false;
            if (object.ReferenceEquals(x, y)) return true;

            return Unsafe.Equals(x, y);
        }

        public int GetHashCode(byte[] value)
        {
            return ItemUtils.GetHashCode(value);
        }
    }

    class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        new public bool Equals(object x, object y)
        {
            return object.ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
