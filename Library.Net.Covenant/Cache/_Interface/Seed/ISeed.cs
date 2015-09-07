using System;

namespace Library.Net.Covenant
{
    public interface ISeed<TKey> : IKeywords, ICompressionAlgorithm, ICryptoAlgorithm
         where TKey : IKey
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        int Rank { get; }
        TKey Key { get; }
    }
}
