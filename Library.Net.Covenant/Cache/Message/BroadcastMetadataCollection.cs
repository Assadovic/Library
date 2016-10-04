﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    sealed class BroadcastMetadataCollection : LockedList<BroadcastMetadata>
    {
        public BroadcastMetadataCollection() : base() { }
        public BroadcastMetadataCollection(int capacity) : base(capacity) { }
        public BroadcastMetadataCollection(IEnumerable<BroadcastMetadata> collections) : base(collections) { }

        protected override bool Filter(BroadcastMetadata item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
