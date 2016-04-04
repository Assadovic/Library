using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    [DataContract(Name = "Metadata", Namespace = "http://Library/Net/Covenant")]
    public sealed class Metadata : ImmutableWarrantItemBase<Metadata>, IMetadata
    {
        private enum SerializeId : byte
        {
            Name = 0,
            Keyword = 1,
            Length = 2,
            CreationTime = 3,
            Key = 4,

            Cash = 5,
            Certificate = 6,
        }

        private volatile string _name;
        private volatile KeywordCollection _keywords;
        private long _length;
        private DateTime _creationTime;
        private volatile Key _key;

        private volatile Cash _cash;
        private volatile Certificate _certificate;

        private volatile int _hashCode;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxKeywordCount = 3;

        public Metadata(string name, IEnumerable<string> keywords, long length, DateTime creationTime, Key key, HashAlgorithm hashAlgorithm, Miner miner, DigitalSignature digitalSignature)
        {
            this.Name = name;
            if (keywords != null) this.ProtectedKeywords.AddRange(keywords);
            this.Length = length;
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
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Name)
                    {
                        this.Name = ItemUtilities.GetString(rangeStream);
                    }
                    else if (id == (byte)SerializeId.Keyword)
                    {
                        this.ProtectedKeywords.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Length)
                    {
                        this.Length = ItemUtilities.GetLong(rangeStream);
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
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Name
            if (this.Name != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Name, this.Name);
            }
            // Keywords
            foreach (var value in this.Keywords)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Keyword, value);
            }
            // Length
            if (this.Length != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Length, this.Length);
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
            return _hashCode;
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

            if (this.Name != other.Name
                || (this.Keywords == null) != (other.Keywords == null)
                || this.Length != other.Length
                || this.CreationTime != other.CreationTime
                || this.Key != other.Key

                || this.Cash != other.Cash
                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Keywords != null && other.Keywords != null)
            {
                if (!CollectionUtilities.Equals(this.Keywords, other.Keywords)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Name;
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

        #region IMetadata

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            private set
            {
                if (value != null && value.Length > Metadata.MaxNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _name = value;
                }
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyKeywords;

        public IEnumerable<string> Keywords
        {
            get
            {
                if (_readOnlyKeywords == null)
                    _readOnlyKeywords = new ReadOnlyCollection<string>(this.ProtectedKeywords.ToArray());

                return _readOnlyKeywords;
            }
        }

        [DataMember(Name = "Keywords")]
        private KeywordCollection ProtectedKeywords
        {
            get
            {
                if (_keywords == null)
                    _keywords = new KeywordCollection(Metadata.MaxKeywordCount);

                return _keywords;
            }
        }

        [DataMember(Name = "Length")]
        public long Length
        {
            get
            {
                return _length;
            }
            private set
            {
                _length = value;
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

                if (value != null)
                {
                    _hashCode = value.GetHashCode();
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}
