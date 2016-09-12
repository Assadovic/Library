using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    sealed class UnicastMetadataCollection : LockedList<UnicastMetadata>
    {
        public UnicastMetadataCollection() : base() { }
        public UnicastMetadataCollection(int capacity) : base(capacity) { }
        public UnicastMetadataCollection(IEnumerable<UnicastMetadata> collections) : base(collections) { }

        protected override bool Filter(UnicastMetadata item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
