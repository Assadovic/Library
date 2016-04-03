using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashItemBase", Namespace = "http://Library/Security")]
    public abstract class MutableCashItemBase<T> : ItemBase<T>, ICash
        where T : MutableCashItemBase<T>
    {
        public virtual void CreateCash(Miner miner)
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

        public virtual int Cost
        {
            get
            {
                return this.VerifyCash();
            }
        }
    }
}
