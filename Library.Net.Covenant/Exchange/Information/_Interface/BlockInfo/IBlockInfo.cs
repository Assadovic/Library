using System;
using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IBlockInfo : IComputeHash
    {
        int BlockLength { get; }
        HashAlgorithm HashAlgorithm { get; }

        ArraySegment<byte> Get(int index);
        int Count { get; }
    }
}
