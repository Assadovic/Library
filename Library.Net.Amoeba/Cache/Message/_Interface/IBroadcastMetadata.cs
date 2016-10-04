using System;

namespace Library.Net.Amoeba
{
    interface IBroadcastMetadata : IComputeHash
    {
        string Type { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
