using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "UnicastMetadata")]
    sealed class UnicastMetadata : ImmutableCertificateItemBase<UnicastMetadata>, IUnicastMetadata
    {
        private enum SerializeId
        {
            Type = 0,
            Signature = 1,
            CreationTime = 2,
            Metadata = 3,

            Certificate = 4,
        }

        private string _type;
        private volatile string _signature;
        private DateTime _creationTime;

        private volatile Metadata _metadata;

        private volatile Certificate _certificate;

        public static readonly int MaxTypeLength = 256;

        internal UnicastMetadata(string type, string signature, DateTime creationTime, Metadata metadata, DigitalSignature digitalSignature)
        {
            this.Type = type;
            this.Signature = signature;
            this.CreationTime = creationTime;
            this.Metadata = metadata;

            this.CreateCertificate(digitalSignature);
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

                    if (id == (int)SerializeId.Type)
                    {
                        this.Type = reader.GetString();
                    }
                    else if (id == (int)SerializeId.Signature)
                    {
                        this.Signature = reader.GetString();
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
                // Signature
                if (this.Signature != null)
                {
                    writer.Write((int)SerializeId.Signature, this.Signature);
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
            if ((object)obj == null || !(obj is UnicastMetadata)) return false;

            return this.Equals((UnicastMetadata)obj);
        }

        public override bool Equals(UnicastMetadata other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Type != other.Type
                || this.Signature != other.Signature
                || this.CreationTime != other.CreationTime
                || this.Metadata != other.Metadata

                || this.Certificate != other.Certificate)
            {
                return false;
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

        [DataMember(Name = "Type")]
        public string Type
        {
            get
            {
                return _type;
            }
            private set
            {
                if (value != null && value.Length > UnicastMetadata.MaxTypeLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                if (value != null && !Library.Security.Signature.Check(value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _signature = value;
                }
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
