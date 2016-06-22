using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashItemBase", Namespace = "http://Library/Security")]
    public abstract class MutableCashItemBase<T> : MutableCertificateItemBase<T>, ICash
        where T : MutableCashItemBase<T>
    {
        public virtual void CreateCash(Miner miner, string signature)
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

        public virtual int Cost
        {
            get
            {
                if (this.Certificate == null) return this.VerifyCash(null);
                else return this.VerifyCash(this.Certificate.ToString());
            }
        }
    }
}
