using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Covenant
{
    public interface IProfile
    {
        DateTime CreationTime { get; }
        int Cost { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
    }
}
