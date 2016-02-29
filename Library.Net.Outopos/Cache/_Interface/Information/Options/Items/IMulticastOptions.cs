using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IMulticastOptions : IComputeHash
    {
        Key Key { get; }
        int Cost { get; }
    }
}
