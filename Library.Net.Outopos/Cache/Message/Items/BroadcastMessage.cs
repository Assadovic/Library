using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Outopos
{
    [DataContract(Name = "BroadcastMessage", Namespace = "http://Library/Net/Outopos")]
    public sealed class BroadcastMessage : ImmutableCertificateItemBase<BroadcastMessage>, IBroadcastHeader, IBroadcastContent
    {
        private enum SerializeId : byte
        {
            CreationTime = 0,

            Cost = 1,
            ExchangePublicKey = 2,
            TrustSignature = 3,
            DeleteSignature = 4,
            Tag = 5,

            Certificate = 6,
        }

        private DateTime _creationTime;

        private volatile int _cost;
        private volatile ExchangePublicKey _exchangePublicKey;
        private volatile SignatureCollection _trustSignatures;
        private volatile SignatureCollection _deleteSignatures;
        private volatile TagCollection _tags;

        private volatile Certificate _certificate;

        public static readonly int MaxTrustSignatureCount = 1024;
        public static readonly int MaxDeleteSignatureCount = 1024;
        public static readonly int MaxTagCount = 256;

        internal BroadcastMessage(DateTime creationTime, int cost, ExchangePublicKey exchangePublicKey, IEnumerable<string> trustSignatures, IEnumerable<string> deleteSignatures, IEnumerable<Tag> tags, DigitalSignature digitalSignature)
        {
            this.CreationTime = creationTime;

            this.Cost = cost;
            this.ExchangePublicKey = exchangePublicKey;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (deleteSignatures != null) this.ProtectedDeleteSignatures.AddRange(deleteSignatures);
            if (tags != null) this.ProtectedTags.AddRange(tags);

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

                    if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }

                    else if (id == (byte)SerializeId.Cost)
                    {
                        this.Cost = ItemUtilities.GetInt(rangeStream);
                    }
                    else if (id == (byte)SerializeId.ExchangePublicKey)
                    {
                        this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        this.ProtectedTrustSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.DeleteSignature)
                    {
                        this.ProtectedDeleteSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Tag)
                    {
                        this.ProtectedTags.Add(Tag.Import(rangeStream, bufferManager));
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

            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }

            // Cost
            if (this.Cost != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Cost, this.Cost);
            }
            // ExchangePublicKey
            if (this.ExchangePublicKey != null)
            {
                using (var stream = this.ExchangePublicKey.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.ExchangePublicKey, stream);
                }
            }
            // TrustSignatures
            foreach (var value in this.TrustSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.TrustSignature, value);
            }
            // DeleteSignatures
            foreach (var value in this.DeleteSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.DeleteSignature, value);
            }
            // Tags
            foreach (var value in this.Tags)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Tag, stream);
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
            return this.CreationTime.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is BroadcastMessage)) return false;

            return this.Equals((BroadcastMessage)obj);
        }

        public override bool Equals(BroadcastMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime

                || this.Cost != other.Cost
                || this.ExchangePublicKey != other.ExchangePublicKey
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.DeleteSignatures == null) != (other.DeleteSignatures == null)
                || (this.Tags == null) != (other.Tags == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.DeleteSignatures != null && other.DeleteSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.DeleteSignatures, other.DeleteSignatures)) return false;
            }

            if (this.Tags != null && other.Tags != null)
            {
                if (!CollectionUtilities.Equals(this.Tags, other.Tags)) return false;
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

        #region IBroadcastHeader

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

        #region IBroadcastContent

        [DataMember(Name = "Cost")]
        public int Cost
        {
            get
            {
                return _cost;
            }
            private set
            {
                _cost = value;
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }
            private set
            {
                _exchangePublicKey = value;
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                if (_readOnlyTrustSignatures == null)
                    _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures.ToArray());

                return _readOnlyTrustSignatures;
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                if (_trustSignatures == null)
                    _trustSignatures = new SignatureCollection(BroadcastMessage.MaxTrustSignatureCount);

                return _trustSignatures;
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyDeleteSignatures;

        public IEnumerable<string> DeleteSignatures
        {
            get
            {
                if (_readOnlyDeleteSignatures == null)
                    _readOnlyDeleteSignatures = new ReadOnlyCollection<string>(this.ProtectedDeleteSignatures.ToArray());

                return _readOnlyDeleteSignatures;
            }
        }

        [DataMember(Name = "DeleteSignatures")]
        private SignatureCollection ProtectedDeleteSignatures
        {
            get
            {
                if (_deleteSignatures == null)
                    _deleteSignatures = new SignatureCollection(BroadcastMessage.MaxDeleteSignatureCount);

                return _deleteSignatures;
            }
        }

        private volatile ReadOnlyCollection<Tag> _readOnlyTags;

        public IEnumerable<Tag> Tags
        {
            get
            {
                if (_readOnlyTags == null)
                    _readOnlyTags = new ReadOnlyCollection<Tag>(this.ProtectedTags.ToArray());

                return _readOnlyTags;
            }
        }

        [DataMember(Name = "Tags")]
        private TagCollection ProtectedTags
        {
            get
            {
                if (_tags == null)
                    _tags = new TagCollection(BroadcastMessage.MaxTagCount);

                return _tags;
            }
        }

        #endregion
    }
}
