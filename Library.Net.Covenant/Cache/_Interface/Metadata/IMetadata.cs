using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface IMetadata
    {
        string Name { get; }
        IEnumerable<string> Keywords { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}
