using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Library.Io;

namespace Library.Net.Covenant
{
    [DataContract(Name = "QueryBlocks", Namespace = "http://Library/Net/Covenant")]
    sealed class QueryBlocks : ItemBase<QueryBlocks>, IQueryBlocks
    {
        private enum SerializeId : byte
        {
            Index = 0,
        }

        private List<int> _indexes;

        public static readonly int MaxIndexCount = 32;

        internal QueryBlocks(IEnumerable<int> indexes)
        {
            if (indexes != null) this.ProtectedIndexes.AddRange(indexes);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                byte id;
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.Index)
                    {
                        this.ProtectedIndexes.Add(ItemUtilities.GetInt(rangeStream));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Index
            foreach (var value in this.Indexes)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Index, value);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return this.ProtectedIndexes.Count;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is QueryBlocks)) return false;

            return this.Equals((QueryBlocks)obj);
        }

        public override bool Equals(QueryBlocks other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.Indexes == null) != (other.Indexes == null))
            {
                return false;
            }

            if (this.Indexes != null && other.Indexes != null)
            {
                if (!CollectionUtilities.Equals(this.Indexes, other.Indexes)) return false;
            }

            return true;
        }

        #region IQueryBlock

        private volatile ReadOnlyCollection<int> _readOnlyIndexes;

        public IEnumerable<int> Indexes
        {
            get
            {
                if (_readOnlyIndexes == null)
                    _readOnlyIndexes = new ReadOnlyCollection<int>(this.ProtectedIndexes.ToArray());

                return _readOnlyIndexes;
            }
        }

        [DataMember(Name = "Indexes")]
        private List<int> ProtectedIndexes
        {
            get
            {
                if (_indexes == null)
                    _indexes = new List<int>(QueryBlocks.MaxIndexCount);

                return _indexes;
            }
        }

        #endregion
    }
}
