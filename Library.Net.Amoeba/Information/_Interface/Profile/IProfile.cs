using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Amoeba
{
    public interface IProfile
    {
        int Limit { get; }
        ExchangePublicKey ExchangePublicKey { get; }
        IEnumerable<Tag> Tags { get; }
        string Comment { get; }
    }
}
