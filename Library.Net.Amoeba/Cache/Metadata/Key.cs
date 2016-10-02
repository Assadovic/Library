using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Library.Io;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Key")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Key : IKey, IEquatable<Key>
    {
        private volatile HashAlgorithm _hashAlgorithm;
        private volatile byte[] _hash;

        public static readonly int MaxHashLength = 32;

        public Key(HashAlgorithm hashAlgorithm, byte[] hash)
        {
            _hashAlgorithm = 0;
            _hash = null;

            this.HashAlgorithm = hashAlgorithm;
            this.Hash = hash;
        }

        public static Key Import(Stream stream, BufferManager bufferManager)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                return new Key((HashAlgorithm)reader.GetId(), reader.GetBytes());
            }
        }

        public Stream Export(BufferManager bufferManager)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                writer.Write((int)this.HashAlgorithm, this.Hash);

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            return ItemUtils.GetHashCode(this.Hash);
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Key)) return false;

            return this.Equals((Key)obj);
        }

        public bool Equals(Key other)
        {
            if (this.HashAlgorithm != other.HashAlgorithm
                || (this.Hash == null) != (other.Hash == null))
            {
                return false;
            }

            if (this.Hash != null && other.Hash != null)
            {
                if (!Unsafe.Equals(this.Hash, other.Hash)) return false;
            }

            return true;
        }

        public static bool operator ==(Key x, Key y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(Key x, Key y)
        {
            return !(x == y);
        }

        #region IKey

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                return _hashAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(HashAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hashAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "Hash")]
        public byte[] Hash
        {
            get
            {
                return _hash;
            }
            private set
            {
                if (value != null && value.Length > Key.MaxHashLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hash = value;
                }
            }
        }

        #endregion
    }
}
