using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Amoeba
{
    public sealed class MessageCollection : LockedList<Message>
    {
        public MessageCollection() : base() { }
        public MessageCollection(int capacity) : base(capacity) { }
        public MessageCollection(IEnumerable<Message> collections) : base(collections) { }

        protected override bool Filter(Message item)
        {
            if (item == null) return true;

            return false;
        }
    }
}
