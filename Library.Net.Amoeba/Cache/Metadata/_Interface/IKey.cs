
namespace Library.Net.Amoeba
{
    interface IKey
    {
        HashAlgorithm HashAlgorithm { get; }
        byte[] Hash { get; }
    }
}
