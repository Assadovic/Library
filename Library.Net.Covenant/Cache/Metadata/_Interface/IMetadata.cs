
namespace Library.Net.Covenant
{
    interface IMetadata
    {
        HashAlgorithm HashAlgorithm { get; }
        byte[] Hash { get; }
    }
}
