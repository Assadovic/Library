using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface ILink
    {
        ICollection<string> TrustSignatures { get; }
        ICollection<string> DeleteSignatures { get; }
    }
}
