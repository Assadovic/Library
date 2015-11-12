using System;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IUnicastOptions : IComputeHash
    {
        Key Key { get; }
        int Cost { get; }
    }
}
