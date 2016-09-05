using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "KeyDerivationAlgorithm")]
    enum KeyDerivationAlgorithm
    {
        [EnumMember(Value = "Pbkdf2")]
        Pbkdf2 = 0x01,
    }
}
