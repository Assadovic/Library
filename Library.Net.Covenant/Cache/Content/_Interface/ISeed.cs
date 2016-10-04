using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
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
