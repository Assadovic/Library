
namespace Library.Net.Amoeba
{
    interface IComputeHash
    {
        byte[] CreateHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
