using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

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

        private volatile int _depth;
        private volatile Key _key;

        private volatile CompressionAlgorithm _compressionAlgorithm;

        private volatile CryptoAlgorithm _cryptoAlgorithm;
        private volatile byte[] _cryptoKey;

        public static readonly int MaxCryptoKeyLength = 256;

        public Metadata(int depth, Key key, CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey)
        {
            this.Depth = depth;
            this.Key = key;
            this.CompressionAlgorithm = compressionAlgorithm;
            this.CryptoAlgorithm = cryptoAlgorithm;
            this.CryptoKey = cryptoKey;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.Depth)
                    {
                        this.Depth = reader.GetInt();
                    }
                    else if (id == (int)SerializeId.Key)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.Key = Key.Import(rangeStream, bufferManager);
                        }
                    }

                    else if (id == (int)SerializeId.CompressionAlgorithm)
                    {
                        this.CompressionAlgorithm = reader.GetEnum<CompressionAlgorithm>();
                    }

                    else if (id == (int)SerializeId.CryptoAlgorithm)
                    {
                        this.CryptoAlgorithm = reader.GetEnum<CryptoAlgorithm>();
                    }
                    else if (id == (int)SerializeId.CryptoKey)
                    {
                        this.CryptoKey = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Depth
                if (this.Depth != 0)
                {
                    writer.Write((int)SerializeId.Depth, this.Depth);
                }
                // Key
                if (this.Key != null)
                {
                    using (var exportStream = this.Key.Export(bufferManager))
                    {
                        writer.Write((int)SerializeId.Key, exportStream);
                    }
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.CompressionAlgorithm, this.CompressionAlgorithm);
                }

                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm);
                }
                // CryptoKey
                if (this.CryptoKey != null)
                {
                    writer.Write((int)SerializeId.CryptoKey, this.CryptoKey);
                }

                return writer.GetStream();
            }
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
