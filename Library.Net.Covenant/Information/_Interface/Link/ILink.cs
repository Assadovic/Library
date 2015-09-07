using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Covenant
{
    public interface ILink
    {
        ICollection<string> TrustSignatures { get; }
    }
}
