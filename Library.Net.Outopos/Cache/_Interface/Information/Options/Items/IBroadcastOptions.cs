using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IBroadcastOptions : IComputeHash
    {
        Key Key { get; }
    }
}
