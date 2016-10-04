using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Covenant
{
    [DataContract(Name = "BlocksInfo", Namespace = "http://Library/Net/Covenant")]
    sealed class BlocksInfo : ItemBase<BlocksInfo>, IBlocksInfo
    {
        private enum SerializeId : byte
        {
            BlockLength = 0,
            HashAlgorithm = 1,
            Hashes = 2,
        }

        private volatile int _blockLength;
        private volatile HashAlgorithm _hashAlgorithm;
        private volatile byte[] _hashes;

        private volatile int _hashCode;

        private static readonly int MaxHashesLength = BitmapManager.MaxLength * 32;

        public BlocksInfo(int blockLength, HashAlgorithm hashAlgorithm, byte[] hashes)
        {
            if (hashAlgorithm == HashAlgorithm.Sha256 && hashes.Length % 32 != 0) throw new ArgumentException(nameof(hashes));

            this.BlockLength = blockLength;
            this.HashAlgorithm = hashAlgorithm;
            this.Hashes = hashes;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.BlockLength)
                    {
                        this.BlockLength = reader.GetInt();
                    }
                    else if (id == (int)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = reader.GetEnum<HashAlgorithm>();
                    }
                    else if (id == (int)SerializeId.Hashes)
                    {
                        this.Hashes = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // BlockLength
                if (this.BlockLength != 0)
                {
                    writer.Write((int)SerializeId.BlockLength, this.BlockLength);
                }
                // HashAlgorithm
                if (this.HashAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.HashAlgorithm, this.HashAlgorithm);
                }
                // Hashes
                if (this.Hashes != null)
                {
                    writer.Write((int)SerializeId.Hashes, this.Hashes);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is BlocksInfo)) return false;

            return this.Equals((BlocksInfo)obj);
        }

        public override bool Equals(BlocksInfo other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.BlockLength != other.BlockLength
                || this.HashAlgorithm != other.HashAlgorithm
                || (this.Hashes == null) != (other.Hashes == null))
            {
                return false;
            }

            if (this.Hashes != null && other.Hashes != null)
            {
                if (!Unsafe.Equals(this.Hashes, other.Hashes)) return false;
            }

            return true;
        }

        #region IBlocksInfo

        [DataMember(Name = "BlockLength")]
        public int BlockLength
        {
            get
            {
                return _blockLength;
            }
            private set
            {
                _blockLength = value;
            }
        }

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

        [DataMember(Name = "Hashes")]
        private byte[] Hashes
        {
            get
            {
                return _hashes;
            }
            set
            {
                if (value != null && value.Length > BlocksInfo.MaxHashesLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _hashes = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtils.GetHashCode(value, 0, 32);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        public ArraySegment<byte> Get(int index)
        {
            if (this.HashAlgorithm == HashAlgorithm.Sha256)
            {
                if ((this.Hashes.Length / 32) <= index) throw new ArgumentOutOfRangeException(nameof(index));

                return new ArraySegment<byte>(this.Hashes, index, 32);
            }
            else
            {
                throw new ArgumentException();
            }
        }

        public int Count
        {
            get
            {
                if (this.HashAlgorithm == HashAlgorithm.Sha256)
                {
                    return this.Hashes.Length / 32;
                }
                else
                {
                    throw new ArgumentException();
                }
            }
        }

        #endregion

        #region IComputeHash

        private volatile byte[] _sha256_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
        {
            if (_sha256_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _sha256_hash = Sha256.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha256)
            {
                return _sha256_hash;
            }

            return null;
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
