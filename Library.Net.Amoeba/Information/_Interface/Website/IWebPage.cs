using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "HypertextFormatType")]
    public enum HypertextFormatType : byte
    {
        [EnumMember(Value = "Markdown")]
        Markdown = 0,
    }
    interface IWebpage
    {
        string Name { get; }
        HypertextFormatType FormatType { get; }
        string Content { get; }
    }
}
