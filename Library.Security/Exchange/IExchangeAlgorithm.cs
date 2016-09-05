using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "ExchangeAlgorithm")]
    public enum ExchangeAlgorithm : byte
    {
        [EnumMember(Value = "Rsa2048")]
        Rsa2048 = 0,
    }
}
