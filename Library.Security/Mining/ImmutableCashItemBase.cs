using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashItemBase", Namespace = "http://Library/Security")]
    public abstract class ImmutableCashItemBase<T> : ItemBase<T>, ICash
        where T : ImmutableCashItemBase<T>
    {
        protected virtual void CreateCash(Miner miner)
        {
            if (miner == null)
            {
                this.Cash = null;
            }
            else
            {
                using (var stream = this.GetCashStream())
                {
                    this.Cash = miner.Create(stream);
                }
            }
        }

        protected virtual int VerifyCash()
        {
            if (this.Cash == null)
            {
                return 0;
            }
            else
            {
                using (var stream = this.GetCashStream())
                {
                    return Miner.Verify(this.Cash, stream);
                }
            }
        }

        protected abstract Stream GetCashStream();

        [DataMember(Name = "Cash")]
        protected abstract Cash Cash { get; set; }

        private volatile int _cost = -1;

        public virtual int Cost
        {
            get
            {
                if (_cost == -1)
                    _cost = this.VerifyCash();

                return _cost;
            }
        }
    }
}
