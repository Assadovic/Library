﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Connection.SecureVersion1
{
    [Flags]
    [DataContract(Name = "KeyExchangeAlgorithm", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    enum KeyExchangeAlgorithm
    {
        [EnumMember(Value = "ECDiffieHellman521_Sha512")]
        ECDiffieHellman521_Sha512 = 0x01,

        [EnumMember(Value = "Rsa2048_Sha512")]
        Rsa2048_Sha512 = 0x02,
    }
}
