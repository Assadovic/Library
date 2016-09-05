﻿using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "HashAlgorithm")]
    public enum HashAlgorithm : byte
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 0,
    }

    public interface IHashAlgorithm
    {
        HashAlgorithm HashAlgorithm { get; }
    }
}
