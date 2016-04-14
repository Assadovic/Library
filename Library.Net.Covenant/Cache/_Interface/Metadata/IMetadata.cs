using System;
using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "MetadataType", Namespace = "http://Library/Net/Covenant")]
    enum MetadataType : byte
    {
        [EnumMember(Value = "Link")]
        Link = 0,

        [EnumMember(Value = "Store")]
        Store = 1,
    }

    interface IMetadata : IComputeHash
    {
        MetadataType Type { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}
