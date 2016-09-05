
namespace Library.Net.Amoeba
{
    interface IMetadata<TKey> : ICompressionAlgorithm, ICryptoAlgorithm
        where TKey : IKey
    {
        int Depth { get; }
        TKey Key { get; }
    }
}
