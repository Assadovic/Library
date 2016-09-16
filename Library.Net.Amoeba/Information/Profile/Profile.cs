using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Profile")]
    public sealed class Profile : ItemBase<Profile>, IProfile
    {
        private enum SerializeId
        {
            ExchangePublicKey = 0,
        }

        private volatile ExchangePublicKey _exchangePublicKey;

        public Profile(ExchangePublicKey exchangePublicKey)
        {
            this.ExchangePublicKey = exchangePublicKey;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.ExchangePublicKey)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // ExchangePublicKey
                if (this.ExchangePublicKey != null)
                {
                    writer.Add((int)SerializeId.ExchangePublicKey, this.ExchangePublicKey.Export(bufferManager));
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.ExchangePublicKey == null) return 0;
            else return this.ExchangePublicKey.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Profile)) return false;

            return this.Equals((Profile)obj);
        }

        public override bool Equals(Profile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.ExchangePublicKey != other.ExchangePublicKey)
            {
                return false;
            }

            return true;
        }

        #region IProfile

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }
            private set
            {
                _exchangePublicKey = value;
            }
        }

        #endregion
    }
}
