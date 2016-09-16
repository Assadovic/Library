using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface ILink
    {
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
    }
}
