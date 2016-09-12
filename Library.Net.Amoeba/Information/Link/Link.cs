using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Link")]
    public sealed class Link : ItemBase<Link>, ILink, ICloneable<Link>, IThisLock
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

        public Link()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    for (;;)
                    {
                        var id = reader.GetId();
                        if (id < 0) return;

                        if (id == (int)SerializeId.TrustSignature)
                        {
                            this.TrustSignatures.Add(reader.GetString());
                        }
                        else if (id == (int)SerializeId.DeleteSignature)
                        {
                            this.DeleteSignatures.Add(reader.GetString());
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
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.TrustSignatures.Count == 0) return 0;
                else return this.TrustSignatures[0].GetHashCode();
            }
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

        ICollection<string> ILink.TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.TrustSignatures;
                }
            }
        }

        [DataMember(Name = "TrustSignatures")]
        public SignatureCollection TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_trustSignatures == null)
                        _trustSignatures = new SignatureCollection(Link.MaxTrustSignatureCount);

                    return _trustSignatures;
                }
            }
        }

        ICollection<string> ILink.DeleteSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.DeleteSignatures;
                }
            }
        }

        [DataMember(Name = "DeleteSignatures")]
        public SignatureCollection DeleteSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_deleteSignatures == null)
                        _deleteSignatures = new SignatureCollection(Link.MaxDeleteSignatureCount);

                    return _deleteSignatures;
                }
            }
        }

        #endregion

        #region ICloneable<Link>

        public Link Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Link.Import(stream, BufferManager.Instance);
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
