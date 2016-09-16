using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class WebpageCollection : LockedList<Webpage>
    {
        public WebpageCollection() : base() { }
        public WebpageCollection(int capacity) : base(capacity) { }
        public WebpageCollection(IEnumerable<Webpage> collections) : base(collections) { }

        protected override bool Filter(Webpage item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
