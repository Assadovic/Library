using System;

namespace Library.Net.Amoeba
{
    interface ITag
    {
        string Name { get; }
        byte[] Id { get; }
    }
}
