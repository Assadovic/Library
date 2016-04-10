using System;
using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "MetadataType", Namespace = "http://Library/Net/Covenant")]
    enum MetadataType : byte
    {
        [EnumMember(Value = "Trust")]
        Trust = 0,

        [EnumMember(Value = "Box")]
        Box = 1,
    }

    interface IMetadata : IComputeHash
    {
        DateTime CreationTime { get; }
        MetadataType Type { get; }
        Key Key { get; }
    }
}
