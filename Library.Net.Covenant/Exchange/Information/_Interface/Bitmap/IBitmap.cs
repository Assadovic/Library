using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IBitmap
    {
        bool Get(int index);
        void Set(int index, bool flag);
        int Length { get; }
    }
}
