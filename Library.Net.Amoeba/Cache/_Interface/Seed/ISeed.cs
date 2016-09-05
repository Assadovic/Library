using System;
using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    interface ISeed<TMetadata, TKey>
        where TMetadata : IMetadata<TKey>
        where TKey : IKey
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        ICollection<string> Keywords { get; }
        TMetadata Metadata { get; }
    }
}
