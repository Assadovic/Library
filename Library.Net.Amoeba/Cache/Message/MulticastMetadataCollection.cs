using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    sealed class MulticastMetadataCollection : LockedList<MulticastMetadata>
    {
        public MulticastMetadataCollection() : base() { }
        public MulticastMetadataCollection(int capacity) : base(capacity) { }
        public MulticastMetadataCollection(IEnumerable<MulticastMetadata> collections) : base(collections) { }

        protected override bool Filter(MulticastMetadata item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
