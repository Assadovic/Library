using System;
using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [Flags]
    [DataContract(Name = "ExchangeProtocolVersion", Namespace = "http://Library/Net/Covenant")]
    enum ExchangeProtocolVersion
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0x01,
    }
}
