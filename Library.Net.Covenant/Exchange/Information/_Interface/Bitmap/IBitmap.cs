using System.Collections.Generic;

namespace Library.Net.Covenant
{
    interface IBitmap<T>
        where T : IBitmap<T>
    {
        bool Get(int index);
        void Set(int index, bool flag);
        int Length { get; }

        T And(T target);
        T Or(T target);
        T Xor(T target);

        byte[] ToBinary();
    }
}
