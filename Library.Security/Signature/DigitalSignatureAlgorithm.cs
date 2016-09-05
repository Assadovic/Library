using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignatureAlgorithm")]
    public enum DigitalSignatureAlgorithm : byte
    {
        [EnumMember(Value = "Rsa2048_Sha256")]
        Rsa2048_Sha256 = 0,

        [EnumMember(Value = "EcDsaP521_Sha256")]
        EcDsaP521_Sha256 = 1,
    }
}
