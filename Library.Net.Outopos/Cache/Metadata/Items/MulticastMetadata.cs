using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "MulticastMetadata", Namespace = "http://Library/Net/Outopos")]
    class MulticastMetadata : ImmutableWarrantItemBase<MulticastMetadata>, IMulticastHeader, IMulticastOptions
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            CreationTime = 1,

            Key = 2,

            Cash = 3,
            Certificate = 4,
        }

        private volatile Tag _tag;
        private DateTime _creationTime;

        private volatile Key _key;

        private volatile Cash _cash;
        private volatile Certificate _certificate;

        internal MulticastMetadata(Tag tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;

            this.Key = key;

            this.CreateCash(miner, digitalSignature?.ToString());
            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                byte id;

                using (var rangeStream = ItemUtilities.GetStream(out id, stream))
                {
                    if (rangeStream == null) return;

                    if (id == (byte)SerializeId.Tag)
                    {
                        this.Tag = Outopos.Tag.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }

                    else if (id == (byte)SerializeId.Key)
                    {
                        this.Key = Key.Import(rangeStream, bufferManager);
                    }

                    else if (id == (byte)SerializeId.Cash)
                    {
                        this.Cash = Cash.Import(rangeStream, bufferManager);
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
            var bufferStream = new BufferStream(bufferManager);

            // Tag
            if (this.Tag != null)
            {
                using (var stream = this.Tag.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Tag, stream);
                }
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

            // Cash
            if (this.Cash != null)
            {
                using (var stream = this.Cash.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Cash, stream);
                }
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
            if (this.Key == null) return 0;
            else return this.Key.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is MulticastMetadata)) return false;

            return this.Equals((MulticastMetadata)obj);
        }

        public override bool Equals(MulticastMetadata other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime

                || this.Key != other.Key

                || this.Cash != other.Cash
                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
        }

        protected override void CreateCash(Miner miner, string signature)
        {
            base.CreateCash(miner, signature);
        }

        protected override int VerifyCash(string signature)
        {
            return base.VerifyCash(signature);
        }

        protected override Stream GetCashStream(string signature)
        {
            var tempCertificate = this.Certificate;
            this.Certificate = null;

            var tempCash = this.Cash;
            this.Cash = null;

            try
            {
                var stream = this.Export(BufferManager.Instance);

                stream.Seek(0, SeekOrigin.End);
                ItemUtilities.Write(stream, (byte)SerializeId.Certificate, signature);
                stream.Seek(0, SeekOrigin.Begin);

                return stream;
            }
            finally
            {
                this.Certificate = tempCertificate;
                this.Cash = tempCash;
            }
        }

        protected override Cash Cash
        {
            get
            {
                return _cash;
            }
            set
            {
                _cash = value;
            }
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

        #region IMulticastHeader<TTag>

        [DataMember(Name = "Tag")]
        public Tag Tag
        {
            get
            {
                return _tag;
            }
            private set
            {
                _tag = value;
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                return _creationTime;
            }
            private set
            {
                var utc = value.ToUniversalTime();
                _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
            }
        }

        #endregion

        #region IMulticastOptions

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
