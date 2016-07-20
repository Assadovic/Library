using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    [DataContract(Name = "BlocksInfo", Namespace = "http://Library/Net/Covenant")]
    public sealed class BlocksInfo : ItemBase<BlocksInfo>, IBlocksInfo
    {
        private enum SerializeId : byte
        {
            BlockLength = 0,
            HashAlgorithm = 1,
            Hashes = 2,
        }

        private volatile int _blockLength;
        private volatile HashAlgorithm _hashAlgorithm = 0;
        private volatile byte[] _hashes;

        private volatile int _hashCode;

        private static readonly int MaxHashesLength = Bitmap.MaxLength * 32;

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
            for (;;)
            {
                byte id;
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.BlockLength)
                    {
                        this.BlockLength = ItemUtilities.GetInt(rangeStream);
                    }
                    else if (id == (byte)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Hashes)
                    {
                        this.Hashes = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // BlockLength
            if (this.BlockLength != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.BlockLength, this.BlockLength);
            }
            // HashAlgorithm
            if (this.HashAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.HashAlgorithm, this.HashAlgorithm.ToString());
            }
            // Hashes
            if (this.Hashes != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Hashes, this.Hashes);
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

        #region IIndex

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
                    _hashCode = ItemUtilities.GetHashCode(value, 0, 32);
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
