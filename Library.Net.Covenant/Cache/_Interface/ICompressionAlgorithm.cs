using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "CompressionAlgorithm", Namespace = "http://Library/Net/Covenant")]
    public enum CompressionAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Xz")]
        Xz = 1,
    }

    public interface ICompressionAlgorithm
    {
        CompressionAlgorithm CompressionAlgorithm { get; }
    }
}
