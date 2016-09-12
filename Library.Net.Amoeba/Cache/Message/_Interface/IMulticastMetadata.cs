using System;

namespace Library.Net.Amoeba
{
    public interface IMulticastMetadata<TTag> : IComputeHash
        where TTag : ITag
    {
        string Type { get; }
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
