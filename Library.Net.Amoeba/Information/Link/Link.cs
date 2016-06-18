using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Link", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Link : ItemBase<Link>, ILink, ICloneable<Link>, IThisLock
    {
        private enum SerializeId : byte
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
                for (;;)
                {
                    byte id;

                    using (var rangeStream = ItemUtilities.GetStream(out id, stream))
                    {
                        if (rangeStream == null) return;

                        if (id == (byte)SerializeId.TrustSignature)
                        {
                            this.TrustSignatures.Add(ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.DeleteSignature)
                        {
                            this.DeleteSignatures.Add(ItemUtilities.GetString(rangeStream));
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

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
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

            if (!CollectionUtilities.Equals(this.TrustSignatures, other.TrustSignatures)
                || !CollectionUtilities.Equals(this.DeleteSignatures, other.DeleteSignatures))
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
