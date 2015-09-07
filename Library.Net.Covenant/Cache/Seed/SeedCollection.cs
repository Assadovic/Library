using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class SeedCollection : LockedList<Seed>
    {
        public SeedCollection() : base() { }
        public SeedCollection(int capacity) : base(capacity) { }
        public SeedCollection(IEnumerable<Seed> collections) : base(collections) { }

        protected override bool Filter(Seed item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
