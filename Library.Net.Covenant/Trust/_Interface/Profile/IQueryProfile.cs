using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Covenant
{
    public interface IQueryProfile
    {
        DateTime CreationTime { get; }
        string Signature { get; }
    }
}
