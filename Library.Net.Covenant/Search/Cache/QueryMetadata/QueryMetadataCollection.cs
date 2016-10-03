using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    sealed class QueryMetadataCollection : LockedList<QueryMetadata>
    {
        public QueryMetadataCollection() : base() { }
        public QueryMetadataCollection(int capacity) : base(capacity) { }
        public QueryMetadataCollection(IEnumerable<QueryMetadata> collections) : base(collections) { }

        protected override bool Filter(QueryMetadata item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
