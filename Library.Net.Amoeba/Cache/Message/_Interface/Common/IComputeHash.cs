
namespace Library.Net.Amoeba
{
    public interface IComputeHash
    {
        byte[] CreateHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
