using System;

namespace Library.Net.Outopos
{
    public interface IUnicastHeader
    {
        string Signature { get; }
        DateTime CreationTime { get; }
    }
}
