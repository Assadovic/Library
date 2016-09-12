using System;

namespace Library.Net.Amoeba
{
    public interface ITag
    {
        string Name { get; }
        byte[] Id { get; }
    }
}
