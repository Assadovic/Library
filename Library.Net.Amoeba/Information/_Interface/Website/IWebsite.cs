using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface IWebsite
    {
        IEnumerable<Webpage> Webpages { get; }
    }
}
