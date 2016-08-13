using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Collections;
using Library.Io;
using Library.Net.Amoeba;
using Library.Security;
using Library.Utilities;

namespace Library.UnitTest
{
    [DataContract(Name = "T_Box", Namespace = "http://Library/Net/Amoeba")]
    public sealed class T_Box : MutableCertificateItemBase<T_Box>, IThisLock
    {
        private enum SerializeId
        {
            Name = 0,
            CreationTime = 1,
            Comment = 2,
            Seed = 3,
            T_Box = 4,

            Certificate = 5,
        }

        private string _name;
        private DateTime _creationTime;
        private string _comment;
        private SeedCollection _seeds;
        private LockedList<T_Box> _boxes;

        private Certificate _certificate;

        private int _hashCode;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxCommentLength = 1024;
        public static readonly int MaxD_BoxCount = 8192;
        public static readonly int MaxSeedCount = 1024 * 64;

        public T_Box()
        {

        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            //if (count > 256) throw new ArgumentException();

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
                        else if (type == (int)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtils.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (type == (int)SerializeId.Comment)
                        {
                            this.Comment = ItemUtils.GetString(rangeStream);
                        }
                        else if (type == (int)SerializeId.Seed)
                        {
                            this.Seeds.Add(Seed.Import(rangeStream, bufferManager));
                        }
                        else if (type == (int)SerializeId.T_Box)
                        {
                            this.T_Boxes.Add(T_Box.Import(rangeStream, bufferManager, count + 1));
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
            if (count > 256) throw new ArgumentException();

            lock (this.ThisLock)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Name
                if (this.Name != null)
                {
                    ItemUtils.Write(bufferStream, (int)SerializeId.Name, this.Name);
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
                // Seeds
                foreach (var value in this.Seeds)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.Seed, stream);
                    }
                }
                // Boxes
                foreach (var value in this.T_Boxes)
                {
                    using (var stream = value.Export(bufferManager, count + 1))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.T_Box, stream);
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
            if ((object)obj == null || !(obj is T_Box)) return false;

            return this.Equals((T_Box)obj);
        }

        public override bool Equals(T_Box other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Name != other.Name
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment

                || this.Certificate != other.Certificate

                || (this.Seeds == null) != (other.Seeds == null)
                || (this.T_Boxes == null) != (other.T_Boxes == null))
            {
                return false;
            }

            if (this.Seeds != null && other.Seeds != null)
            {
                if (!CollectionUtils.Equals(this.Seeds, other.Seeds)) return false;
            }

            if (this.T_Boxes != null && other.T_Boxes != null)
            {
                if (!CollectionUtils.Equals(this.T_Boxes, other.T_Boxes)) return false;
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

        #region T_Box

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
                    if (value != null && value.Length > T_Box.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                        _hashCode = _name.GetHashCode();
                    }
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
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _comment;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > T_Box.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        [DataMember(Name = "Seeds")]
        public SeedCollection Seeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_seeds == null)
                        _seeds = new SeedCollection(T_Box.MaxSeedCount);

                    return _seeds;
                }
            }
        }

        [DataMember(Name = "T_Boxes")]
        public LockedList<T_Box> T_Boxes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_boxes == null)
                        _boxes = new LockedList<T_Box>(T_Box.MaxD_BoxCount);

                    return _boxes;
                }
            }
        }

        #endregion

        #region ICloneable<T_Box>

        public T_Box Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return T_Box.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #endregion
    }
}
