using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Link")]
    public sealed class Link : ItemBase<Link>, ILink
    {
        private enum SerializeId
        {
            TrustSignature = 0,
            DeleteSignature = 1,
        }

        private SignatureCollection _trustSignatures;
        private SignatureCollection _deleteSignatures;

        private volatile object _thisLock;

        public static readonly int MaxTrustSignatureCount = 1024;
        public static readonly int MaxDeleteSignatureCount = 1024;

        public Link(IEnumerable<string> trustSignatures, IEnumerable<string> deleteSignatures)
        {
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (deleteSignatures != null) this.ProtectedDeleteSignatures.AddRange(deleteSignatures);
        }

        protected override void Initialize()
        {
            base.Initialize();

            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.TrustSignature)
                    {
                        this.ProtectedTrustSignatures.Add(reader.GetString());
                    }
                    else if (id == (int)SerializeId.DeleteSignature)
                    {
                        this.ProtectedTrustSignatures.Add(reader.GetString());
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // TrustSignatures
                foreach (var value in this.TrustSignatures)
                {
                    writer.Write((int)SerializeId.TrustSignature, value);
                }
                // DeleteSignatures
                foreach (var value in this.DeleteSignatures)
                {
                    writer.Write((int)SerializeId.DeleteSignature, value);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.ProtectedTrustSignatures.Count == 0) return 0;
            else return this.ProtectedTrustSignatures[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Link)) return false;

            return this.Equals((Link)obj);
        }

        public override bool Equals(Link other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtils.Equals(this.TrustSignatures, other.TrustSignatures)
                || !CollectionUtils.Equals(this.DeleteSignatures, other.DeleteSignatures))
            {
                return false;
            }

            return true;
        }

        #region ILink

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
                    _trustSignatures = new SignatureCollection(Link.MaxTrustSignatureCount);

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
                    _deleteSignatures = new SignatureCollection(Link.MaxDeleteSignatureCount);

                return _deleteSignatures;
            }
        }

        #endregion
    }
}
