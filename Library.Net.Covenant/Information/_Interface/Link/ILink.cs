using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface ILink
    {
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
    }
}
