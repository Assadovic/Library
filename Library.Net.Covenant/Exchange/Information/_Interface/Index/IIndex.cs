using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IIndex : IComputeHash
    {
        int BlockLength { get; }
        HashAlgorithm HashAlgorithm { get; }

        ArraySegment<byte> Get(int index);
        int Count { get; }
    }
}
