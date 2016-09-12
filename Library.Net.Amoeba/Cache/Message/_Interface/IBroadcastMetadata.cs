using System;

namespace Library.Net.Amoeba
{
    public interface IBroadcastMetadata : IComputeHash
    {
        string Type { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
