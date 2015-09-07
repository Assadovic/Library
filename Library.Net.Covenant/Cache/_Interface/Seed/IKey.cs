
namespace Library.Net.Covenant
{
    public interface IKey : IHashAlgorithm
    {
        byte[] Hash { get; }
    }
}
