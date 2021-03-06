﻿using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CertificateItemBase")]
    public abstract class ImmutableCertificateItemBase<T> : ItemBase<T>, ICertificate
        where T : ImmutableCertificateItemBase<T>
    {
        protected virtual void CreateCertificate(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null)
            {
                this.Certificate = null;
            }
            else
            {
                using (var stream = this.GetCertificateStream())
                {
                    this.Certificate = new Certificate(digitalSignature, stream);
                }
            }
        }

        public virtual bool VerifyCertificate()
        {
            if (this.Certificate == null)
            {
                return true;
            }
            else
            {
                using (var stream = this.GetCertificateStream())
                {
                    return this.Certificate.Verify(stream);
                }
            }
        }

        protected abstract Stream GetCertificateStream();

        [DataMember(Name = "Certificate")]
        public abstract Certificate Certificate { get; protected set; }
    }
}
