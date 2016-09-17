using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Seed")]
    public sealed class Seed : MutableCertificateItemBase<Seed>, ISeed, ICloneable<Seed>, IThisLock
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
        private volatile Metadata _metadata;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxKeywordCount = 3;

        public Seed(Metadata metadata)
        {
            this.Metadata = metadata;
        }

        protected override void Initialize()
        {
            base.Initialize();

            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Name)
                        {
                            this.Name = reader.GetString();
                        }
                        else if (id == (int)SerializeId.Length)
                        {
                            this.Length = reader.GetLong();
                        }
                        else if (id == (int)SerializeId.CreationTime)
                        {
                            this.CreationTime = reader.GetDateTime();
                        }
                        else if (id == (int)SerializeId.Keyword)
                        {
                            this.Keywords.Add(reader.GetString());
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
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Name
                    if (this.Name != null)
                    {
                        writer.Write((int)SerializeId.Name, this.Name);
                    }
                    // Length
                    if (this.Length != 0)
                    {
                        writer.Write((int)SerializeId.Length, this.Length);
                    }
                    // CreationTime
                    if (this.CreationTime != DateTime.MinValue)
                    {
                        writer.Write((int)SerializeId.CreationTime, this.CreationTime);
                    }
                    // Keywords
                    foreach (var value in this.Keywords)
                    {
                        writer.Write((int)SerializeId.Keyword, value);
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
        }

        public override int GetHashCode()
        {
            if (this.Metadata == null) return 0;
            else return this.Metadata.GetHashCode();
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

        #region ISeed

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

        ICollection<string> ISeed.Keywords
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
                return _metadata;
            }
            private set
            {
                _metadata = value;
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
