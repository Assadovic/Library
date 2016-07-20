using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Covenant
{
    interface IQueryMetadata
    {
        MetadataType Type { get; }
        DateTime CreationTime { get; }
        string Signature { get; }
    }
}
