﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class KeyCollection : LockedList<Key>
    {
        public KeyCollection() : base() { }
        public KeyCollection(int capacity) : base(capacity) { }
        public KeyCollection(IEnumerable<Key> collections) : base(collections) { }

        protected override bool Filter(Key item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
