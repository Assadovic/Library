using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface ILocation
    {
        DateTime CreationTime { get; }
        Key Key { get; }
        IEnumerable<string> Uris { get; }
    }
}
