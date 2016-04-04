using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;


namespace Library.Net.Covenant
{
    [DataContract(Name = "Profile", Namespace = "http://Library/Net/Covenant")]
    public sealed class Profile : ImmutableCertificateItemBase<Profile>, IProfile
    {
        private enum SerializeId : byte
        {
            CreationTime = 0,
            Cost = 1,
            TrustSignature = 2,
            DeleteSignature = 3,

            Certificate = 4,
        }

        private DateTime _creationTime;
        private volatile int _cost;
        private volatile SignatureCollection _trustSignatures;
        private volatile SignatureCollection _deleteSignatures;

        private volatile Certificate _certificate;

        public static readonly int MaxTrustSignatureCount = 1024;
        public static readonly int MaxDeleteSignatureCount = 1024;

        internal Profile(DateTime creationTime, int cost, IEnumerable<string> trustSignatures, IEnumerable<string> deleteSignatures, DigitalSignature digitalSignature)
        {
            this.CreationTime = creationTime;
            this.Cost = cost;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (deleteSignatures != null) this.ProtectedDeleteSignatures.AddRange(deleteSignatures);

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
                    if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }

                    else if (id == (byte)SerializeId.Cost)
                    {
                        this.Cost = ItemUtilities.GetInt(rangeStream);
                    }
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        this.ProtectedTrustSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.DeleteSignature)
                    {
                        this.ProtectedDeleteSignatures.Add(ItemUtilities.GetString(rangeStream));
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
            if ((object)obj == null || !(obj is Profile)) return false;

            return this.Equals((Profile)obj);
        }

        public override bool Equals(Profile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || this.Cost != other.Cost
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.DeleteSignatures == null) != (other.DeleteSignatures == null)

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

        #region IProfile

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
                    _trustSignatures = new SignatureCollection(Profile.MaxTrustSignatureCount);

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
                    _deleteSignatures = new SignatureCollection(Profile.MaxDeleteSignatureCount);

                return _deleteSignatures;
            }
        }

        #endregion
    }
}
