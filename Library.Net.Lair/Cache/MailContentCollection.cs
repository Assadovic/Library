﻿using System.Collections.Generic;
using Library.Collections;

namespace Library.Net.Lair
{
    public sealed class MailContentCollection : FilterList<MailContent>, IEnumerable<MailContent>
    {
        public MailContentCollection() : base() { }
        public MailContentCollection(int capacity) : base(capacity) { }
        public MailContentCollection(IEnumerable<MailContent> collections) : base(collections) { }

        protected override bool Filter(MailContent item)
        {
            if (item == null) return true;

            return false;
        }

        #region IEnumerable<MailContent>

        IEnumerator<MailContent> IEnumerable<MailContent>.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return base.GetEnumerator();
            }
        }

        #endregion

        #region IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (base.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion
    }
}
