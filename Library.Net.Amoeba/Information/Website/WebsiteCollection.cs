using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class WebsiteCollection : LockedList<Website>
    {
        public WebsiteCollection() : base() { }
        public WebsiteCollection(int capacity) : base(capacity) { }
        public WebsiteCollection(IEnumerable<Website> collections) : base(collections) { }

        protected override bool Filter(Website item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
