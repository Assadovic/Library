﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class LinkCollection : LockedList<Link>
    {
        public LinkCollection() : base() { }
        public LinkCollection(int capacity) : base(capacity) { }
        public LinkCollection(IEnumerable<Link> collections) : base(collections) { }

        protected override bool Filter(Link item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
