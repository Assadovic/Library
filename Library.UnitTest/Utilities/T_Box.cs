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
    public sealed class T_Box : ItemBase<T_Box>, IThisLock
    {
        private enum SerializeId
        {
            Name = 0,
            Seed = 1,
            T_Box = 2,
        }

        private string _name;
        private SeedCollection _seeds;
        private LockedList<T_Box> _boxes;

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
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) > 0)

                        if (id == (int)SerializeId.Name)
                        {
                            this.Name = reader.GetString();
                        }
                        else if (id == (int)SerializeId.Seed)
                        {
                            this.Seeds.Add(Seed.Import(reader.GetStream(), bufferManager));
                        }
                        else if (id == (int)SerializeId.T_Box)
                        {
                            this.T_Boxes.Add(T_Box.Import(reader.GetStream(), bufferManager, count + 1));
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
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Name
                    if (this.Name != null)
                    {
                        writer.Write((int)SerializeId.Name, this.Name);
                    }
                    // Seeds
                    foreach (var value in this.Seeds)
                    {
                        writer.Add((int)SerializeId.Seed, value.Export(bufferManager));
                    }
                    // Boxes
                    foreach (var value in this.T_Boxes)
                    {
                        writer.Add((int)SerializeId.T_Box, value.Export(bufferManager, count + 1));
                    }

                    return writer.GetStream();
                }
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
