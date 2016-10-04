using System;

namespace Library.Net.Amoeba
{
    interface IUnicastMetadata : IComputeHash
    {
        string Type { get; }
        string Signature { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
