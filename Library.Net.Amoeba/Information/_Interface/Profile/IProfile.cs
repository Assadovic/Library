using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Amoeba
{
    public interface IProfile
    {
        ExchangePublicKey ExchangePublicKey { get; }
    }
}
