using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Store", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Store : ItemBase<Store>, IStore, ICloneable<Store>, IThisLock
    {
        private enum SerializeId : byte
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
                for (;;)
                {
                    byte id;

                    using (var rangeStream = ItemUtilities.GetStream(out id, stream))
                    {
                        if (rangeStream == null) return;

                        if (id == (byte)SerializeId.Box)
                        {
                            this.Boxes.Add(Box.Import(rangeStream, bufferManager));
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

                // Boxes
                foreach (var value in this.Boxes)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Box, stream);
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

            if (!CollectionUtilities.Equals(this.Boxes, other.Boxes))
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
