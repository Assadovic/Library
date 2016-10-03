using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    sealed class LocationCollection : LockedList<Location>
    {
        public LocationCollection() : base() { }
        public LocationCollection(int capacity) : base(capacity) { }
        public LocationCollection(IEnumerable<Location> collections) : base(collections) { }

        protected override bool Filter(Location item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
