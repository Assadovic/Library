using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CompressionAlgorithm")]
    public enum CompressionAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Xz")]
        Xz = 1,
    }

    interface ICompressionAlgorithm
    {
        CompressionAlgorithm CompressionAlgorithm { get; }
    }
}
