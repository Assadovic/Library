using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IStore
    {
        ICollection<Box> Boxes { get; }
    }
}
