using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "CryptoAlgorithm")]
    enum CryptoAlgorithm
    {
        [EnumMember(Value = "Aes256")]
        Aes256 = 0x01,
    }
}
