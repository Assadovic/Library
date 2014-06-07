using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        int Cash { get; }
        Key Key { get; }
    }
}
