using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Outopos
{
    [DataContract(Name = "MulticastMessage", Namespace = "http://Library/Net/Outopos")]
    public sealed class MulticastMessage : ImmutableCertificateItemBase<MulticastMessage>, IMulticastHeader, IMulticastContent
    {
        private enum SerializeId
        {
            Tag = 0,
            CreationTime = 1,

            Comment = 2,

            Certificate = 3,
        }

        private volatile Tag _tag;
        private DateTime _creationTime;

        private volatile string _comment;

        private volatile Certificate _certificate;

        public static readonly int MaxCommentLength = 1024 * 8;

        internal MulticastMessage(Tag tag, DateTime creationTime, string comment, DigitalSignature digitalSignature)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;

            this.Comment = comment;

            this.CreateCertificate(digitalSignature);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                int type;

                using (var rangeStream = ItemUtils.GetStream(out type, stream))
                {
                    if (rangeStream == null) return;

                    if (type == (int)SerializeId.Tag)
                    {
                        this.Tag = Outopos.Tag.Import(rangeStream, bufferManager);
                    }
                    else if (type == (int)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtils.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }

                    else if (type == (int)SerializeId.Comment)
                    {
                        this.Comment = ItemUtils.GetString(rangeStream);
                    }

                    else if (type == (int)SerializeId.Certificate)
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
                    ItemUtils.Write(bufferStream, (int)SerializeId.Tag, stream);
                }
            }
            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }

            // Comment
            if (this.Comment != null)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.Comment, this.Comment);
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

        public override int GetHashCode()
        {
            return this.CreationTime.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is MulticastMessage)) return false;

            return this.Equals((MulticastMessage)obj);
        }

        public override bool Equals(MulticastMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime

                || this.Comment != other.Comment

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

        #region IMulticastHeader

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

        #region IMulticastContent

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > MulticastMessage.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        #endregion
    }
}
