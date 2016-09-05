using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface IStore
    {
        ICollection<Box> Boxes { get; }
    }
}
