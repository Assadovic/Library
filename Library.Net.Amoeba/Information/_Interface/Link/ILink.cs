using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    public interface ILink
    {
        ICollection<string> TrustSignatures { get; }
    }
}
