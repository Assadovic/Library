using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Group")]
    sealed class Group : ItemBase<Group>, IGroup<Key>, ICloneable<Group>, IThisLock
    {
        private enum SerializeId
        {
            Key = 0,

            CorrectionAlgorithm = 1,
            InformationLength = 2,
            BlockLength = 3,
            Length = 4,
        }

        private KeyCollection _keys;

        private CorrectionAlgorithm _correctionAlgorithm;
        private int _informationLength;
        private int _blockLength;
        private long _length;

        private volatile object _thisLock;

        public Group()
        {

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
                        if (id == (int)SerializeId.Key)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.Keys.Add(Key.Import(rangeStream, bufferManager));
                            }
                        }

                        else if (id == (int)SerializeId.CorrectionAlgorithm)
                        {
                            this.CorrectionAlgorithm = reader.GetEnum<CorrectionAlgorithm>();
                        }
                        else if (id == (int)SerializeId.InformationLength)
                        {
                            this.InformationLength = reader.GetInt();
                        }
                        else if (id == (int)SerializeId.BlockLength)
                        {
                            this.BlockLength = reader.GetInt();
                        }
                        else if (id == (int)SerializeId.Length)
                        {
                            this.Length = reader.GetLong();
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
                    // Keys
                    foreach (var value in this.Keys)
                    {
                        writer.Add((int)SerializeId.Key, value.Export(bufferManager));
                    }

                    // CorrectionAlgorithm
                    if (this.CorrectionAlgorithm != 0)
                    {
                        writer.Write((int)SerializeId.CorrectionAlgorithm, this.CorrectionAlgorithm);
                    }
                    // InformationLength
                    if (this.InformationLength != 0)
                    {
                        writer.Write((int)SerializeId.InformationLength, this.InformationLength);
                    }
                    // BlockLength
                    if (this.BlockLength != 0)
                    {
                        writer.Write((int)SerializeId.BlockLength, this.BlockLength);
                    }
                    // Length
                    if (this.Length != 0)
                    {
                        writer.Write((int)SerializeId.Length, this.Length);
                    }

                    return writer.GetStream();
                }
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Keys.Count == 0) return 0;
                else return this.Keys[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Group)) return false;

            return this.Equals((Group)obj);
        }

        public override bool Equals(Group other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtils.Equals(this.Keys, other.Keys)

                || this.CorrectionAlgorithm != other.CorrectionAlgorithm
                || this.InformationLength != other.InformationLength
                || this.BlockLength != other.BlockLength
                || this.Length != other.Length)
            {
                return false;
            }

            return true;
        }

        #region IGroup<Key>

        ICollection<Key> IGroup<Key>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keys;
                }
            }
        }

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keys == null)
                        _keys = new KeyCollection();

                    return _keys;
                }
            }
        }

        #endregion

        #region ICorrectionAlgorithm

        [DataMember(Name = "CorrectionAlgorithm")]
        public CorrectionAlgorithm CorrectionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _correctionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CorrectionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _correctionAlgorithm = value;
                    }
                }
            }
        }

        [DataMember(Name = "InformationLength")]
        public int InformationLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _informationLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _informationLength = value;
                }
            }
        }

        [DataMember(Name = "BlockLength")]
        public int BlockLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _blockLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _blockLength = value;
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

        #endregion

        #region ICloneable<Group>

        public Group Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Group.Import(stream, BufferManager.Instance);
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
