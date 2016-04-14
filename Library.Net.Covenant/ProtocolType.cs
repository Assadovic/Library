using System;
using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "ProtocolType", Namespace = "http://Library/Net/Covenant")]
    enum ProtocolType
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Search")]
        Search = 1,

        [EnumMember(Value = "Exchange")]
        Exchange = 2,
    }
}
