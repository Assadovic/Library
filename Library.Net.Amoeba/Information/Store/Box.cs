﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Box")]
    public sealed class Box : ItemBase<Box>, IBox, ICloneable<Box>, IThisLock
    {
        private enum SerializeId
        {
            Name = 0,
            Seed = 1,
            Box = 2,
        }

        private string _name;
        private SeedCollection _seeds;
        private BoxCollection _boxes;

        private volatile int _hashCode;

        private volatile object _thisLock;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxSeedCount = 1024 * 64;
        public static readonly int MaxBoxCount = 8192;

        public Box()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            if (count > 256) throw new ArgumentException();

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
                        else if (type == (int)SerializeId.Seed)
                        {
                            this.Seeds.Add(Seed.Import(rangeStream, bufferManager));
                        }
                        else if (type == (int)SerializeId.Box)
                        {
                            this.Boxes.Add(Box.Import(rangeStream, bufferManager, count + 1));
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
                // Seeds
                foreach (var value in this.Seeds)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.Seed, stream);
                    }
                }
                // Boxes
                foreach (var value in this.Boxes)
                {
                    using (var stream = value.Export(bufferManager, count + 1))
                    {
                        ItemUtils.Write(bufferStream, (int)SerializeId.Box, stream);
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
            if ((object)obj == null || !(obj is Box)) return false;

            return this.Equals((Box)obj);
        }

        public override bool Equals(Box other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Name != other.Name
                || !CollectionUtils.Equals(this.Seeds, other.Seeds)
                || !CollectionUtils.Equals(this.Boxes, other.Boxes))
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

        #region IBox

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
                    if (value != null && value.Length > Box.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }

                    if (value != null)
                    {
                        _hashCode = value.GetHashCode();
                    }
                    else
                    {
                        _hashCode = 0;
                    }
                }
            }
        }

        ICollection<Seed> IBox.Seeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Seeds;
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
                        _seeds = new SeedCollection(Box.MaxSeedCount);

                    return _seeds;
                }
            }
        }

        ICollection<Box> IBox.Boxes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Boxes;
                }
            }
        }

        [DataMember(Name = "Boxes")]
        public BoxCollection Boxes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_boxes == null)
                        _boxes = new BoxCollection(Box.MaxBoxCount);

                    return _boxes;
                }
            }
        }

        #endregion

        #region ICloneable<Box>

        public Box Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Box.Import(stream, BufferManager.Instance);
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
