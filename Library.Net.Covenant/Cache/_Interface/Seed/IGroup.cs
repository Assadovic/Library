using System.Collections.Generic;

namespace Library.Net.Covenant
{
    public interface IGroup<TKey> : ICorrectionAlgorithm
          where TKey : IKey
    {
        ICollection<TKey> Keys { get; }
    }
}
