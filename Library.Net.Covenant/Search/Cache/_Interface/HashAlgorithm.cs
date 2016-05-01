using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Covenant")]
    public enum HashAlgorithm : byte
    {
        [EnumMember(Value = "Sha256")]
        Sha256 = 0,
    }
}
