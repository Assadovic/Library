﻿using System;
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
    [DataContract(Name = "ProtocolInformation", Namespace = "http://Library/Net/Connection/SecureVersion3")]
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
            for (;;)
            {
                int type;

                using (var rangeStream = ItemUtilities.GetStream(out type, stream))
                {
                    if (rangeStream == null) return;

                    if (type == (int)SerializeId.KeyExchangeAlgorithm)
                    {
                        this.KeyExchangeAlgorithm = EnumEx<KeyExchangeAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.KeyDerivationAlgorithm)
                    {
                        this.KeyDerivationAlgorithm = EnumEx<KeyDerivationAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.CryptoAlgorithm)
                    {
                        this.CryptoAlgorithm = EnumEx<CryptoAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.HashAlgorithm)
                    {
                        this.HashAlgorithm = EnumEx<HashAlgorithm>.Parse(ItemUtilities.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.SessionId)
                    {
                        this.SessionId = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // KeyExchangeAlgorithm
            if (this.KeyExchangeAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.KeyExchangeAlgorithm, this.KeyExchangeAlgorithm.ToString());
            }
            // KeyDerivationAlgorithm
            if (this.KeyDerivationAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.KeyDerivationAlgorithm, this.KeyDerivationAlgorithm.ToString());
            }
            // CryptoAlgorithm
            if (this.CryptoAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm.ToString());
            }
            // HashAlgorithm
            if (this.HashAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.HashAlgorithm, this.HashAlgorithm.ToString());
            }
            // SessionId
            if (this.SessionId != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.SessionId, this.SessionId);
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
                    _hashCode = ItemUtilities.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }
    }
}
