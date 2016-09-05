using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashItemBase")]
    public abstract class ImmutableCashItemBase<T> : ImmutableCertificateItemBase<T>, ICash
        where T : ImmutableCashItemBase<T>
    {
        protected virtual void CreateCash(Miner miner, string signature)
        {
            if (miner == null)
            {
                this.Cash = null;
            }
            else
            {
                using (var stream = this.GetCashStream(signature))
                {
                    this.Cash = miner.Create(stream);
                }
            }
        }

        protected virtual int VerifyCash(string signature)
        {
            if (this.Cash == null)
            {
                return 0;
            }
            else
            {
                using (var stream = this.GetCashStream(signature))
                {
                    return Miner.Verify(this.Cash, stream);
                }
            }
        }

        protected abstract Stream GetCashStream(string signature);

        [DataMember(Name = "Cash")]
        protected abstract Cash Cash { get; set; }

        private volatile int _cost = -1;

        public virtual int Cost
        {
            get
            {
                if (_cost == -1)
                {
                    if (this.Certificate == null) _cost = this.VerifyCash(null);
                    else _cost = this.VerifyCash(this.Certificate.ToString());
                }

                return _cost;
            }
        }
    }
}
