using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Outopos
{
    public sealed class ChatCollection : LockedList<Chat>
    {
        public ChatCollection() : base() { }
        public ChatCollection(int capacity) : base(capacity) { }
        public ChatCollection(IEnumerable<Chat> collections) : base(collections) { }

        protected override bool Filter(Chat item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
