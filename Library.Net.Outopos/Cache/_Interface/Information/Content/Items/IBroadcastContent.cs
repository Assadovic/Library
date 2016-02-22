using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IBroadcastContent
    {
        int Cost { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<string> TrustSignatures { get; }
        IEnumerable<string> DeleteSignatures { get; }
        IEnumerable<Tag> Tags { get; }
    }
}
