﻿using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Amoeba
{
    public sealed class SignatureCollection : FilterList<string>, IEnumerable<string>
    {
        public SignatureCollection() : base() { }
        public SignatureCollection(int capacity) : base(capacity) { }
        public SignatureCollection(IEnumerable<string> collections) : base(collections) { }

        protected override bool Filter(string item)
        {
            return Signature.HasSignature(item);
        }

        #region IEnumerable<string>

        IEnumerator<string> IEnumerable<string>.GetEnumerator()
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
