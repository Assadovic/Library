using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Seed")]
    public sealed class Seed : MutableCertificateItemBase<Seed>, ISeed<Metadata, Key>, ICloneable<Seed>, IThisLock
    {
        private enum SerializeId
        {
            Name = 0,
            Length = 1,
            CreationTime = 2,
            Keyword = 3,
            Metadata = 4,

            Certificate = 5,
        }

        private string _name;
        private long _length;
        private DateTime _creationTime;
        private KeywordCollection _keywords;
        private Metadata _metadata;

        private Certificate _certificate;

        private volatile int _hashCode;

        private volatile object _thisLock;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxKeywordCount = 3;

        public Seed(Metadata metadata)
        {
            this.Metadata = metadata;
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                for (;;)
                {
                    int type;

                    using (var rangeStream = ItemUtils.GetStream(out type, stream))
                    {
                        if (rangeStream == null) return;

                        if (type == (int)SerializeId.Name)
                        {
                            this.Name = ItemUtils.GetString(rangeStream);
                        }
                        else if (type == (int)SerializeId.Length)
                        {
                            this.Length = ItemUtils.GetLong(rangeStream);
                        }
                        else if (type == (int)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtils.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (type == (int)SerializeId.Keyword)
                        {
                            this.Keywords.Add(ItemUtils.GetString(rangeStream));
                        }
                        else if (type == (int)SerializeId.Metadata)
                        {
                            this.Metadata = Metadata.Import(rangeStream, bufferManager);
                        }

                        else if (type == (int)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Name
                if (this.Name != null)
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.Name, this.Name);
                }
                // Length
                if (this.Length != 0)
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.Length, this.Length);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // Keywords
                foreach (var value in this.Keywords)
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.Keyword, value);
                }
                // Metadata
                if (this.Metadata != null)
                {
                    using (var stream = this.Metadata.Export(bufferManager))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.Metadata, stream);
                    }
                }

                // Certificate
                if (this.Certificate != null)
                {
                    using (var stream = this.Certificate.Export(bufferManager))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.Certificate, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Seed)) return false;

            return this.Equals((Seed)obj);
        }

        public override bool Equals(Seed other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Name != other.Name
                || this.Length != other.Length
                || this.CreationTime != other.CreationTime
                || this.Metadata != other.Metadata

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return this.Name;
            }
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

        #region ISeed<Metadata, Key>

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Seed.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }
                }
            }
        }

        [DataMember(Name = "Length")]
        public long Length
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _length;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _length = value;
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

        ICollection<string> ISeed<Metadata, Key>.Keywords
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keywords;
                }
            }
        }

        [DataMember(Name = "Keywords")]
        public KeywordCollection Keywords
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keywords == null)
                        _keywords = new KeywordCollection(Seed.MaxKeywordCount);

                    return _keywords;
                }
            }
        }

        [DataMember(Name = "Metadata")]
        public Metadata Metadata
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _metadata;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _metadata = value;

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
        }

        #endregion

        #region ICloneable<Seed>

        public Seed Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Seed.Import(stream, BufferManager.Instance);
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
