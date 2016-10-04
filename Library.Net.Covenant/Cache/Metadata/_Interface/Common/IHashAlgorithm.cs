using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "HashAlgorithm")]
    public enum HashAlgorithm : byte
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 1,
    }
}
