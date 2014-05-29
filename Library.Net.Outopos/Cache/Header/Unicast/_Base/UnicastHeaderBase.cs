using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "UnicastHeaderBase", Namespace = "http://Library/Net/Outopos")]
    public abstract class UnicastHeaderBase<TUnicastHeader> : ImmutableCertificateItemBase<TUnicastHeader>, IUnicastHeader
        where TUnicastHeader : UnicastHeaderBase<TUnicastHeader>
    {
        private enum SerializeId : byte
        {
            Signature = 0,
            CreationTime = 1,
            Key = 2,
            Option = 3,

            Certificate = 4,
        }

        private volatile string _signature;
        private DateTime _creationTime;
        private volatile Key _key;
        private byte[] _option;

        private volatile Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxOptionLength = 256;

        public UnicastHeaderBase(string signature, DateTime creationTime, Key key, byte[] option, DigitalSignature digitalSignature)
        {
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Key = key;
            this.Option = option;

            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Signature)
                    {
                        this.Signature = ItemUtilities.GetString(rangeStream);
                    }
                    else if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }
                    else if (id == (byte)SerializeId.Key)
                    {
                        this.Key = Key.Import(rangeStream, bufferManager);
                    }

                    else if (id == (byte)SerializeId.Option)
                    {
                        this.Option = ItemUtilities.GetByteArray(rangeStream);
                    }

                    else if (id == (byte)SerializeId.Certificate)
                    {
                        this.Certificate = Certificate.Import(rangeStream, bufferManager);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Signature
            if (this.Signature != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, this.Signature);
            }
            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }
            // Key
            if (this.Key != null)
            {
                using (var stream = this.Key.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                }
            }

            // Option
            if (this.Option != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Option, this.Option);
            }

            // Certificate
            if (this.Certificate != null)
            {
                using (var stream = this.Certificate.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Certificate, stream);
                }
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _key.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is TUnicastHeader)) return false;

            return this.Equals((TUnicastHeader)obj);
        }

        public override bool Equals(TUnicastHeader other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.Key != other.Key

                || (this.Option == null) != (other.Option == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Option != null && other.Option != null)
            {
                if (!Unsafe.Equals(this.Option, other.Option)) return false;
            }

            return true;
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            base.CreateCertificate(digitalSignature);
        }

        public override bool VerifyCertificate()
        {
            return base.VerifyCertificate();
        }

        protected override Stream GetCertificateStream()
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

        public override Certificate Certificate
        {
            get
            {
                return _certificate;
            }
            protected set
            {
                _certificate = value;
            }
        }

        #region IUnicastHeader

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                _signature = value;
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return _creationTime;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
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

        [DataMember(Name = "Option")]
        public byte[] Option
        {
            get
            {
                return _option;
            }
            private set
            {
                if (value != null && value.Length > UnicastHeaderBase<TUnicastHeader>.MaxOptionLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _option = value;
                }
            }
        }

        #endregion

        #region IComputeHash

        private volatile byte[] _sha512_hash;

        public byte[] GetHash(HashAlgorithm hashAlgorithm)
        {
            if (_sha512_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _sha512_hash = Sha512.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha512)
            {
                return _sha512_hash;
            }

            return null;
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Unsafe.Equals(this.GetHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
