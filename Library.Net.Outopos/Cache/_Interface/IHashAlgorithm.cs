using System.Runtime.Serialization;

namespace Library.Net.Outopos
{
    [DataContract(Name = "HashAlgorithm")]
    public enum HashAlgorithm : byte
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 0,
    }
}
