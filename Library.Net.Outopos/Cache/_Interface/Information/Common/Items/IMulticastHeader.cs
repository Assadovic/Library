using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMulticastHeader
    {
        Tag Tag { get; }
        DateTime CreationTime { get; }
    }
}
