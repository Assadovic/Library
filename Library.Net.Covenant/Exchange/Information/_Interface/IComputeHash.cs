
namespace Library.Net.Covenant
{
    public interface IComputeHash
    {
        byte[] CreateHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
