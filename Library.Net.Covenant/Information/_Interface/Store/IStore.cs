
using System.Collections.Generic;
namespace Library.Net.Covenant
{
    public interface IStore
    {
        ICollection<Box> Boxes { get; }
    }
}
