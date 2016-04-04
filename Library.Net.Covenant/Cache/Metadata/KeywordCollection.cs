using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Library.Collections;

namespace Library.Net.Covenant
{
    public sealed class KeywordCollection : LockedList<string>
    {
        public KeywordCollection() : base() { }
        public KeywordCollection(int capacity) : base(capacity) { }
        public KeywordCollection(IEnumerable<string> collections) : base(collections) { }

        public static readonly int MaxKeywordLength = 256;

        protected override bool Filter(string item)
        {
            if (item == null || item.Length > KeywordCollection.MaxKeywordLength) return true;

            return false;
        }
    }
}
