using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface IBroadcastContent
    {
        int Cost { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
    }
}
