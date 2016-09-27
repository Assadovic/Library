using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class KeyCollection : LockedList<Key>
    {
        public KeyCollection() : base() { }
        public KeyCollection(int capacity) : base(capacity) { }
        public KeyCollection(IEnumerable<Key> collections) : base(collections) { }

        protected override bool Filter(Key item)
        {
            if (item == default(Key)) return true;

            return false;
        }
    }
}
