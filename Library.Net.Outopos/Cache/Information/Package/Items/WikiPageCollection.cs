using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class WikiPageCollection : LockedList<WikiPage>
    {
        public WikiPageCollection() : base() { }
        public WikiPageCollection(int capacity) : base(capacity) { }
        public WikiPageCollection(IEnumerable<WikiPage> collections) : base(collections) { }

        protected override bool Filter(WikiPage item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
