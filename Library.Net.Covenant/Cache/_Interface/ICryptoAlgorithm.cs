using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "CryptoAlgorithm", Namespace = "http://Library/Net/Covenant")]
    public enum CryptoAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Aes256")]
        Aes256 = 1,
    }

    public interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
