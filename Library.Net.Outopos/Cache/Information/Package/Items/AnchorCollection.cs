using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    sealed class AnchorCollection : LockedList<Anchor>
    {
        public AnchorCollection() : base() { }
        public AnchorCollection(int capacity) : base(capacity) { }
        public AnchorCollection(IEnumerable<Anchor> collections) : base(collections) { }

        protected override bool Filter(Anchor item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
