using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class MetadataCollection : LockedList<Metadata>
    {
        public MetadataCollection() : base() { }
        public MetadataCollection(int capacity) : base(capacity) { }
        public MetadataCollection(IEnumerable<Metadata> collections) : base(collections) { }

        protected override bool Filter(Metadata item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
