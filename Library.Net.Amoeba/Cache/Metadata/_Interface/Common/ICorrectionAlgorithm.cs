using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CorrectionAlgorithm")]
    enum CorrectionAlgorithm : byte
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "ReedSolomon8")]
        ReedSolomon8 = 1,
    }

    interface ICorrectionAlgorithm
    {
        CorrectionAlgorithm CorrectionAlgorithm { get; }
        int InformationLength { get; }
        int BlockLength { get; }
        long Length { get; }
    }
}
