using System;

namespace Library.Net.Amoeba
{
    public interface IUnicastMetadata : IComputeHash
    {
        string Type { get; }
        string Signature { get; }
        DateTime CreationTime { get; }
        Metadata Metadata { get; }
    }
}
