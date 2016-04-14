using System;

namespace Library.Net.Covenant
{
    public interface ISeed
    {
        string Name { get; }
        long Length { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}
