﻿namespace Library.Net.Outopos
{
    interface IMulticastOptions : IComputeHash
    {
        Key Key { get; }
        int Cost { get; }
    }
}
