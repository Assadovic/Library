using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "MulticastMetadata")]
    sealed class MulticastMetadata : ImmutableCashItemBase<MulticastMetadata>, IMulticastMetadata<Tag>
    {
        private enum SerializeId
        {
            Type = 0,
            Tag = 1,
            CreationTime = 2,
            Metadata = 3,

            Cash = 4,
            Certificate = 5,
        }

        private string _type;
        private volatile Tag _tag;
        private DateTime _creationTime;

        private volatile Metadata _metadata;

        private volatile Cash _cash;
        private volatile Certificate _certificate;

        public static readonly int MaxTypeLength = 256;

        internal MulticastMetadata(string type, Tag tag, DateTime creationTime, Metadata metadata, Miner miner, DigitalSignature digitalSignature)
        {
            this.Type = type;
            this.Tag = tag;
            this.CreationTime = creationTime;
            this.Metadata = metadata;

            this.CreateCash(miner, digitalSignature?.ToString());
            this.CreateCertificate(digitalSignature);
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
                    if (id == (int)SerializeId.Type)
                    {
                        this.Type = reader.GetString();
                    }
                    else if (id == (int)SerializeId.Tag)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.Tag = Amoeba.Tag.Import(rangeStream, bufferManager);
                        }
                    }
                    else if (id == (int)SerializeId.CreationTime)
                    {
                        this.CreationTime = reader.GetDateTime();
                    }

                    else if (id == (int)SerializeId.Metadata)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.Metadata = Metadata.Import(rangeStream, bufferManager);
                        }
                    }

                    else if (id == (int)SerializeId.Cash)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.Cash = Cash.Import(rangeStream, bufferManager);
                        }
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

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Type
                if (this.Type != null)
                {
                    writer.Write((int)SerializeId.Type, this.Type);
                }
                // Tag
                if (this.Tag != null)
                {
                    writer.Add((int)SerializeId.Tag, this.Tag.Export(bufferManager));
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    writer.Write((int)SerializeId.CreationTime, this.CreationTime);
                }

                // Metadata
                if (this.Metadata != null)
                {
                    writer.Add((int)SerializeId.Metadata, this.Metadata.Export(bufferManager));
                }

                // Cash
                if (this.Cash != null)
                {
                    writer.Add((int)SerializeId.Cash, this.Cash.Export(bufferManager));
                }
                // Certificate
                if (this.Certificate != null)
                {
                    writer.Add((int)SerializeId.Certificate, this.Certificate.Export(bufferManager));
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.Metadata == null) return 0;
            else return this.Metadata.GetHashCode();
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

            if (this.Type != other.Type
                || this.Tag != other.Tag
                || this.CreationTime != other.CreationTime
                || this.Metadata != other.Metadata

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
                var bufferManager = BufferManager.Instance;
                var streams = new List<Stream>();

                streams.Add(this.Export(bufferManager));

                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    writer.Write((int)SerializeId.Certificate, signature);

                    streams.Add(writer.GetStream());
                }

                return new UniteStream(streams);
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

        #region IMulticastHeader<Tag>

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                return _type;
            }
            private set
            {
                if (value != null && value.Length > MulticastMetadata.MaxTypeLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _type = value;
                }
            }
        }

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

        [DataMember(Name = "Metadata")]
        public Metadata Metadata
        {
            get
            {
                return _metadata;
            }
            private set
            {
                _metadata = value;
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
