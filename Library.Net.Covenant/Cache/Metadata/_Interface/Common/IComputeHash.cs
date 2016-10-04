
namespace Library.Net.Covenant
{
    interface IComputeHash
    {
        byte[] CreateHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
