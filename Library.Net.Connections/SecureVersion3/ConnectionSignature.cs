using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections.SecureVersion3
{
    [DataContract(Name = "ConnectionSignature")]
    sealed class ConnectionSignature : MutableCertificateItemBase<ConnectionSignature>, ICloneable<ConnectionSignature>, IThisLock
    {
        private enum SerializeId
        {
            CreationTime = 0,
            ExchangeKey = 1,
            ProtocolHash = 2,

            Certificate = 3,
        }

        private DateTime _creationTime;
        private byte[] _exchangeKey;
        private byte[] _protocolHash;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxExchangeKeyLength = 8192;
        public static readonly int MaxProtocolHashLength = 32;

        public ConnectionSignature()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    for (;;)
                    {
                        var id = reader.GetId();
                        if (id < 0) return;

                        if (id == (int)SerializeId.CreationTime)
                        {
                            this.CreationTime = reader.GetDateTime();
                        }
                        if (id == (int)SerializeId.ExchangeKey)
                        {
                            this.ExchangeKey = reader.GetBytes();
                        }
                        if (id == (int)SerializeId.ProtocolHash)
                        {
                            this.ProtocolHash = reader.GetBytes();
                        }

                        else if (id == (int)SerializeId.Certificate)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.Certificate = Certificate.Import(rangeStream, bufferManager);
                            }
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // CreationTime
                    if (this.CreationTime != DateTime.MinValue)
                    {
                        writer.Write((int)SerializeId.CreationTime, this.CreationTime);
                    }
                    // ExchangeKey
                    if (this.ExchangeKey != null)
                    {
                        writer.Write((int)SerializeId.ExchangeKey, this.ExchangeKey);
                    }
                    // ProtocolHash
                    if (this.ProtocolHash != null)
                    {
                        writer.Write((int)SerializeId.ProtocolHash, this.ProtocolHash);
                    }

                    // Certificate
                    if (this.Certificate != null)
                    {
                        writer.Add((int)SerializeId.Certificate, this.Certificate.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return this.CreationTime.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ConnectionSignature)) return false;

            return this.Equals((ConnectionSignature)obj);
        }

        public override bool Equals(ConnectionSignature other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || (this.ExchangeKey == null) != (other.ExchangeKey == null)
                || (this.ProtocolHash == null) != (other.ProtocolHash == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.ExchangeKey != null && other.ExchangeKey != null)
            {
                if (!Unsafe.Equals(this.ExchangeKey, other.ExchangeKey)) return false;
            }

            if (this.ProtocolHash != null && other.ProtocolHash != null)
            {
                if (!Unsafe.Equals(this.ProtocolHash, other.ProtocolHash)) return false;
            }

            return true;
        }

        public override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (this.ThisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (this.ThisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    return this.Export(BufferManager.Instance);
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (this.ThisLock)
                {
                    _certificate = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        [DataMember(Name = "ExchangeKey")]
        public byte[] ExchangeKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangeKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxExchangeKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _exchangeKey = value;
                    }
                }
            }
        }

        [DataMember(Name = "ProtocolHash")]
        public byte[] ProtocolHash
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _protocolHash;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxProtocolHashLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _protocolHash = value;
                    }
                }
            }
        }

        #region ICloneable<ConnectionSignature>

        public ConnectionSignature Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ConnectionSignature.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
