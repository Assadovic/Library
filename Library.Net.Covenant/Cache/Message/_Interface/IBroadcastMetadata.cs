using System;

namespace Library.Net.Covenant
{
    interface IBroadcastMetadata : IComputeHash
    {
        string Type { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
