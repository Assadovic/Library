
namespace Library.Net.Outopos
{
    public interface IKey
    {
        HashAlgorithm HashAlgorithm { get; }
        byte[] Hash { get; }
    }
}
