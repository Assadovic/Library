using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Connections.SecureVersion3
{
    [DataContract(Name = "ProtocolInformation")]
    class ProtocolInformation : ItemBase<ProtocolInformation>
    {
        private enum SerializeId
        {
            KeyExchangeAlgorithm = 0,
            KeyDerivationAlgorithm = 1,
            CryptoAlgorithm = 2,
            HashAlgorithm = 3,
            SessionId = 4,
        }

        private volatile KeyExchangeAlgorithm _keyExchangeAlgorithm;
        private volatile KeyDerivationAlgorithm _keyDerivationAlgorithm;
        private volatile CryptoAlgorithm _cryptoAlgorithm;
        private volatile HashAlgorithm _hashAlgorithm;
        private volatile byte[] _sessionId;

        private volatile int _hashCode;

        public static readonly int MaxSessionIdLength = 32;

        public ProtocolInformation(KeyExchangeAlgorithm keyExchangeAlgorithm, KeyDerivationAlgorithm keyDerivationAlgorithm, CryptoAlgorithm cryptoAlgorithm, HashAlgorithm hashAlgorithm, byte[] sessionId)
        {
            this.KeyExchangeAlgorithm = keyExchangeAlgorithm;
            this.KeyDerivationAlgorithm = keyDerivationAlgorithm;
            this.CryptoAlgorithm = cryptoAlgorithm;
            this.HashAlgorithm = hashAlgorithm;
            this.SessionId = sessionId;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                for (;;)
                {
                    var id = reader.GetId();
                    if (id < 0) return;

                    if (id == (int)SerializeId.KeyExchangeAlgorithm)
                    {
                        this.KeyExchangeAlgorithm = reader.GetEnum<KeyExchangeAlgorithm>();
                    }
                    else if (id == (int)SerializeId.KeyDerivationAlgorithm)
                    {
                        this.KeyDerivationAlgorithm = reader.GetEnum<KeyDerivationAlgorithm>();
                    }
                    else if (id == (int)SerializeId.CryptoAlgorithm)
                    {
                        this.CryptoAlgorithm = reader.GetEnum<CryptoAlgorithm>();
                    }
                    else if (id == (int)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = reader.GetEnum<HashAlgorithm>();
                    }
                    else if (id == (int)SerializeId.SessionId)
                    {
                        this.SessionId = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // KeyExchangeAlgorithm
                if (this.KeyExchangeAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.KeyExchangeAlgorithm, this.KeyExchangeAlgorithm);
                }
                // KeyDerivationAlgorithm
                if (this.KeyDerivationAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.KeyDerivationAlgorithm, this.KeyDerivationAlgorithm);
                }
                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm);
                }
                // HashAlgorithm
                if (this.HashAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.HashAlgorithm, this.HashAlgorithm);
                }
                // SessionId
                if (this.SessionId != null)
                {
                    writer.Write((int)SerializeId.SessionId, this.SessionId);
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
            if ((object)obj == null || !(obj is ProtocolInformation)) return false;

            return this.Equals((ProtocolInformation)obj);
        }

        public override bool Equals(ProtocolInformation other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.KeyExchangeAlgorithm != other.KeyExchangeAlgorithm
                || this.KeyDerivationAlgorithm != other.KeyDerivationAlgorithm
                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || this.HashAlgorithm != other.HashAlgorithm
                || (this.SessionId == null) != (other.SessionId == null))
            {
                return false;
            }

            if (this.SessionId != null && other.SessionId != null)
            {
                if (!Unsafe.Equals(this.SessionId, other.SessionId)) return false;
            }

            return true;
        }

        [DataMember(Name = "KeyExchangeAlgorithm")]
        public KeyExchangeAlgorithm KeyExchangeAlgorithm
        {
            get
            {
                return _keyExchangeAlgorithm;
            }
            private set
            {
                _keyExchangeAlgorithm = value;
            }
        }

        [DataMember(Name = "KeyDerivationAlgorithm")]
        public KeyDerivationAlgorithm KeyDerivationAlgorithm
        {
            get
            {
                return _keyDerivationAlgorithm;
            }
            private set
            {
                _keyDerivationAlgorithm = value;
            }
        }

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                return _cryptoAlgorithm;
            }
            private set
            {
                _cryptoAlgorithm = value;
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
                _hashAlgorithm = value;
            }
        }

        [DataMember(Name = "SessionId")]
        public byte[] SessionId
        {
            get
            {
                return _sessionId;
            }
            private set
            {
                if (value != null && value.Length > ProtocolInformation.MaxSessionIdLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _sessionId = value;
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
    }
}
