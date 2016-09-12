using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Index")]
    sealed class Index : ItemBase<Index>, IIndex<Group, Key>, ICloneable<Index>, IThisLock
    {
        private enum SerializeId
        {
            Group = 0,

            CompressionAlgorithm = 1,

            CryptoAlgorithm = 2,
            CryptoKey = 3,
        }

        private GroupCollection _groups;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey;

        private volatile object _thisLock;

        public static readonly int MaxCryptoKeyLength = 256;

        public Index()
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

                        if (id == (int)SerializeId.Group)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.Groups.Add(Group.Import(rangeStream, bufferManager));
                            }
                        }

                        else if (id == (int)SerializeId.CompressionAlgorithm)
                        {
                            this.CompressionAlgorithm = reader.GetEnum<CompressionAlgorithm>();
                        }

                        else if (id == (int)SerializeId.CryptoAlgorithm)
                        {
                            this.CryptoAlgorithm = reader.GetEnum<CryptoAlgorithm>();
                        }
                        else if (id == (int)SerializeId.CryptoKey)
                        {
                            this.CryptoKey = reader.GetBytes();
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
                    // Groups
                    foreach (var value in this.Groups)
                    {
                        writer.Add((int)SerializeId.Group, value.Export(bufferManager));
                    }

                    // CompressionAlgorithm
                    if (this.CompressionAlgorithm != 0)
                    {
                        writer.Write((int)SerializeId.CompressionAlgorithm, this.CompressionAlgorithm);
                    }

                    // CryptoAlgorithm
                    if (this.CryptoAlgorithm != 0)
                    {
                        writer.Write((int)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm);
                    }
                    // CryptoKey
                    if (this.CryptoKey != null)
                    {
                        writer.Write((int)SerializeId.CryptoKey, this.CryptoKey);
                    }

                    return writer.GetStream();
                }
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Groups.Count == 0) return 0;
                else if (this.Groups[0].Keys.Count == 0) return 0;
                else return this.Groups[0].Keys[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Index)) return false;

            return this.Equals((Index)obj);
        }

        public override bool Equals(Index other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtils.Equals(this.Groups, other.Groups)

                || this.CompressionAlgorithm != other.CompressionAlgorithm

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Unsafe.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        #region IIndex<Group, Key>

        ICollection<Group> IIndex<Group, Key>.Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Groups;
                }
            }
        }

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
                }
            }
        }

        #endregion

        #region ICompressionAlgorithm

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CompressionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _compressionAlgorithm = value;
                    }
                }
            }
        }

        #endregion

        #region ICryptoAlgorithm

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CryptoAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoAlgorithm = value;
                    }
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Index.MaxCryptoKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoKey = value;
                    }
                }
            }
        }

        #endregion

        #region ICloneable<Index>

        public Index Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Index.Import(stream, BufferManager.Instance);
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
