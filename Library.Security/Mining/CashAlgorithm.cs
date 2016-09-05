using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashAlgorithm")]
    public enum CashAlgorithm : byte
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0,
    }
}
