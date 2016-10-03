using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    sealed class QueryBlocksCollection : LockedList<QueryBlocks>
    {
        public QueryBlocksCollection() : base() { }
        public QueryBlocksCollection(int capacity) : base(capacity) { }
        public QueryBlocksCollection(IEnumerable<QueryBlocks> collections) : base(collections) { }

        protected override bool Filter(QueryBlocks item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
