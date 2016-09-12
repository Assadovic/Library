using System;
using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface ISeed
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        ICollection<string> Keywords { get; }
        Metadata Metadata { get; }
    }
}
