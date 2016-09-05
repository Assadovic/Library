using System.Runtime.Serialization;

namespace Library.Net.Connections
{
    [DataContract(Name = "SecureConnectionType")]
    public enum SecureConnectionType
    {
        [EnumMember(Value = "Connect")]
        Connect = 0,

        [EnumMember(Value = "Accept")]
        Accept = 1,
    }
}
