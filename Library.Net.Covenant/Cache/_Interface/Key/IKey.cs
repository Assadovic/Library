using Library.Net.Covenant;

namespace Library.Net.Covenant
{
    public interface IKey
    {
        HashAlgorithm HashAlgorithm { get; }
        byte[] Hash { get; }
    }
}
