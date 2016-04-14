using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface IStore
    {
        ICollection<Seed> Seeds { get; }
        ICollection<Box> Boxes { get; }
    }
}
