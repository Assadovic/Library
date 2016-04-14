using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface ILocation
    {
        Key Key { get; }
        IEnumerable<string> Uris { get; }
    }
}
