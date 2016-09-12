using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CryptoAlgorithm")]
    public enum CryptoAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Aes256")]
        Aes256 = 1,
    }

    interface ICryptoAlgorithm
    {
        CryptoAlgorithm CryptoAlgorithm { get; }
        byte[] CryptoKey { get; }
    }
}
