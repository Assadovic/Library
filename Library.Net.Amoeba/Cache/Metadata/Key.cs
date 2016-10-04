using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Key")]
    public sealed class Key : ItemBase<Key>, IKey
    {
        private volatile HashAlgorithm _hashAlgorithm;
        private volatile byte[] _hash;

        public static readonly int MaxHashLength = 32;

        public Key(HashAlgorithm hashAlgorithm, byte[] hash)
        {
            this.HashAlgorithm = hashAlgorithm;
            this.Hash = hash;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                this.HashAlgorithm = (HashAlgorithm)reader.GetId();
                this.Hash = reader.GetBytes();
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                writer.Write((int)this.HashAlgorithm, this.Hash);

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.Hash == null) return 0;
            else return ItemUtils.GetHashCode(this.Hash);
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Key)) return false;

            return this.Equals((Key)obj);
        }

        public override bool Equals(Key other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

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
