﻿using System;
using System.Runtime.Serialization;

namespace Library.Net.Connections.SecureVersion3
{
    [Flags]
    [DataContract(Name = "KeyExchangeAlgorithm")]
    enum KeyExchangeAlgorithm
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0x01,

        [EnumMember(Value = "EcDiffieHellmanP521")]
        EcDiffieHellmanP521 = 0x02,
    }
}
