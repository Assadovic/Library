using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class QueryProfileCollection : LockedList<QueryProfile>
    {
        public QueryProfileCollection() : base() { }
        public QueryProfileCollection(int capacity) : base(capacity) { }
        public QueryProfileCollection(IEnumerable<QueryProfile> collections) : base(collections) { }

        protected override bool Filter(QueryProfile item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
