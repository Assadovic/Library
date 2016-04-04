using System;
using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [Flags]
    [DataContract(Name = "ProtocolVersion", Namespace = "http://Library/Net/Covenant")]
    enum ProtocolVersion
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0x01,
    }
}
