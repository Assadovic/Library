using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Covenant
{
    interface IQueryBlocks
    {
        IEnumerable<int> Indexes { get; }
    }
}
