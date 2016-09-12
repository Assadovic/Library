using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Utilities;

namespace Library.Security
{
    [DataContract(Name = "ExchangePublicKey")]
    public sealed class ExchangePublicKey : ItemBase<ExchangePublicKey>, IExchangeEncrypt
    {
        private enum SerializeId
        {
            CreationTime = 0,
            ExchangeAlgorithm = 1,
            PublicKey = 2,
        }

        private DateTime _creationTime;
        private volatile ExchangeAlgorithm _exchangeAlgorithm = 0;
        private volatile byte[] _publicKey;

        private volatile int _hashCode;

        public static readonly int MaxPublickeyLength = 1024 * 8;

        public ExchangePublicKey(Exchange exchange)
        {
            this.CreationTime = exchange.CreationTime;
            this.ExchangeAlgorithm = exchange.ExchangeAlgorithm;
            this.PublicKey = exchange.PublicKey;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                for (;;)
                {
                    var id = reader.GetId();
                    if (id < 0) return;

                    if (id == (int)SerializeId.CreationTime)
                    {
                        this.CreationTime = reader.GetDateTime();
                    }
                    else if (id == (int)SerializeId.ExchangeAlgorithm)
                    {
                        this.ExchangeAlgorithm = reader.GetEnum<ExchangeAlgorithm>();
                    }
                    else if (id == (int)SerializeId.PublicKey)
                    {
                        this.PublicKey = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    writer.Write((int)SerializeId.CreationTime, this.CreationTime);
                }
                // ExchangeAlgorithm
                if (this.ExchangeAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.ExchangeAlgorithm, this.ExchangeAlgorithm);
                }
                // PublicKey
                if (this.PublicKey != null)
                {
                    writer.Write((int)SerializeId.PublicKey, this.PublicKey);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ExchangePublicKey)) return false;

            return this.Equals((ExchangePublicKey)obj);
        }

        public override bool Equals(ExchangePublicKey other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Unsafe.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            return true;
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                return _creationTime;
            }
            set
            {
                var utc = value.ToUniversalTime();
                _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
            }
        }

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                return _exchangeAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(ExchangeAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                return _publicKey;
            }
            private set
            {
                if (value != null && value.Length > Exchange.MaxPublickeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _publicKey = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtils.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }
    }
}
