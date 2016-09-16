using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Store")]
    public sealed class Store : ItemBase<Store>, IStore, ICloneable<Store>, IThisLock
    {
        private enum SerializeId
        {
            Box = 0,
        }

        private BoxCollection _boxes;

        private volatile object _thisLock;

        public static readonly int MaxBoxCount = 8192;

        public Store()
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
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Box)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.Boxes.Add(Box.Import(rangeStream, bufferManager));
                            }
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
                    // Boxes
                    foreach (var value in this.Boxes)
                    {
                        writer.Add((int)SerializeId.Box, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Boxes.Count == 0) return 0;
                else return this.Boxes[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Store)) return false;

            return this.Equals((Store)obj);
        }

        public override bool Equals(Store other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtils.Equals(this.Boxes, other.Boxes))
            {
                return false;
            }

            return true;
        }

        #region IStore

        ICollection<Box> IStore.Boxes
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
                        _boxes = new BoxCollection(Store.MaxBoxCount);

                    return _boxes;
                }
            }
        }

        #endregion

        #region ICloneable<Store>

        public Store Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Store.Import(stream, BufferManager.Instance);
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
