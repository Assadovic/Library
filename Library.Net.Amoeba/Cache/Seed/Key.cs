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
        private enum SerializeId
        {
            HashAlgorithm = 0,
            Hash = 1,
        }

        private volatile HashAlgorithm _hashAlgorithm = 0;
        private volatile byte[] _hash;

        private volatile int _hashCode;

        public static readonly int MaxHashLength = 32;

        public Key(HashAlgorithm hashAlgorithm, byte[] hash)
        {
            this.HashAlgorithm = hashAlgorithm;
            this.Hash = hash;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                int type;

                using (var rangeStream = ItemUtils.GetStream(out type, stream))
                {
                    if (rangeStream == null) return;

                    if (type == (int)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), ItemUtils.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.Hash)
                    {
                        this.Hash = ItemUtils.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // HashAlgorithm
            if (this.HashAlgorithm != 0)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.HashAlgorithm, this.HashAlgorithm.ToString());
            }
            // Hash
            if (this.Hash != null)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.Hash, this.Hash);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _hashCode;
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

                if (value != null)
                {
                    _hashCode = ItemUtils.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}
