using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IBox
    {
        string Name { get; }
        ICollection<Seed> Seeds { get; }
        ICollection<Box> Boxes { get; }
    }
}
