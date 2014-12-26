using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class WikiCollection : LockedList<Wiki>
    {
        public WikiCollection() : base() { }
        public WikiCollection(int capacity) : base(capacity) { }
        public WikiCollection(IEnumerable<Wiki> collections) : base(collections) { }

        protected override bool Filter(Wiki item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
