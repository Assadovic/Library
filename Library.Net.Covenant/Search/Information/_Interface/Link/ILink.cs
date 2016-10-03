using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface ILink
    {
        ICollection<string> TrustSignatures { get; }
        ICollection<string> DeleteSignatures { get; }
    }
}
