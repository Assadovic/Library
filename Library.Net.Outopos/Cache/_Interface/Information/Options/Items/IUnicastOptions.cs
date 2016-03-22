namespace Library.Net.Outopos
{
    interface IUnicastOptions : IComputeHash
    {
        Key Key { get; }
    }
}
