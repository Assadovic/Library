using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface IIndex<TGroup, TKey> : ICompressionAlgorithm, ICryptoAlgorithm
        where TGroup : IGroup<TKey>
        where TKey : IKey
    {
        ICollection<TGroup> Groups { get; }
    }
}
