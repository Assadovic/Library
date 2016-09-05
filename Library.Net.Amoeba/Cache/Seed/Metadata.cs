using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Metadata")]
    public sealed class Metadata : ItemBase<Metadata>, IMetadata<Key>
    {
        private enum SerializeId
        {
            Depth = 0,
            Key = 1,

            CompressionAlgorithm = 2,

            CryptoAlgorithm = 3,
            CryptoKey = 4,
        }

        private int _depth;
        private Key _key;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey;

        public static readonly int MaxCryptoKeyLength = 256;

        public Metadata(int depth, Key key, CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey)
        {
            this.Depth = depth;
            this.Key = key;
            this.CompressionAlgorithm = compressionAlgorithm;
            this.CryptoAlgorithm = cryptoAlgorithm;
            this.CryptoKey = cryptoKey;
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

                    if (type == (int)SerializeId.Depth)
                    {
                        this.Depth = ItemUtils.GetInt(rangeStream);
                    }
                    else if (type == (int)SerializeId.Key)
                    {
                        this.Key = Key.Import(rangeStream, bufferManager);
                    }

                    else if (type == (int)SerializeId.CompressionAlgorithm)
                    {
                        this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), ItemUtils.GetString(rangeStream));
                    }

                    else if (type == (int)SerializeId.CryptoAlgorithm)
                    {
                        this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), ItemUtils.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.CryptoKey)
                    {
                        this.CryptoKey = ItemUtils.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Depth
            if (this.Depth != 0)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.Depth, this.Depth);
            }
            // Key
            if (this.Key != null)
            {
                using (var stream = this.Key.Export(bufferManager))
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.Key, stream);
                }
            }

            // CompressionAlgorithm
            if (this.CompressionAlgorithm != 0)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.CompressionAlgorithm, this.CompressionAlgorithm.ToString());
            }

            // CryptoAlgorithm
            if (this.CryptoAlgorithm != 0)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm.ToString());
            }
            // CryptoKey
            if (this.CryptoKey != null)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.CryptoKey, this.CryptoKey);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            if (this.Key == null) return 0;
            else return this.Key.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Metadata)) return false;

            return this.Equals((Metadata)obj);
        }

        public override bool Equals(Metadata other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Depth != other.Depth
                || this.Key != other.Key

                || this.CompressionAlgorithm != other.CompressionAlgorithm

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Unsafe.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        #region IMetadata<Key>

        [DataMember(Name = "Depth")]
        public int Depth
        {
            get
            {
                return _depth;
            }
            private set
            {
                _depth = value;
            }
        }

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }

        #endregion

        #region ICompressionAlgorithm

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                return _compressionAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(CompressionAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _compressionAlgorithm = value;
                }
            }
        }

        #endregion

        #region ICryptoAlgorithm

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                return _cryptoAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(CryptoAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                return _cryptoKey;
            }
            private set
            {
                if (value != null && value.Length > Metadata.MaxCryptoKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cryptoKey = value;
                }
            }
        }

        #endregion
    }
}
