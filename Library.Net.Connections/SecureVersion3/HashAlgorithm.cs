using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "HashAlgorithm")]
    enum HashAlgorithm
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 0x01,
    }
}
