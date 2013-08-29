﻿using System;

namespace Library.Net.Lair
{
    interface IVote<TSection, TKey> : IComputeHash
        where TSection : ISection
        where TKey : IKey
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}
