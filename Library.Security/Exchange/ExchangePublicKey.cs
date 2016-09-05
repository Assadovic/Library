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
            for (;;)
            {
                int type;

                using (var rangeStream = ItemUtils.GetStream(out type, stream))
                {
                    if (rangeStream == null) return;

                    if (type == (int)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtils.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }
                    else if (type == (int)SerializeId.ExchangeAlgorithm)
                    {
                        this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), ItemUtils.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.PublicKey)
                    {
                        this.PublicKey = ItemUtils.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }
            // ExchangeAlgorithm
            if (this.ExchangeAlgorithm != 0)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.ExchangeAlgorithm, this.ExchangeAlgorithm.ToString());
            }
            // PublicKey
            if (this.PublicKey != null)
            {
                ItemUtils.Write(bufferStream, (int)SerializeId.PublicKey, this.PublicKey);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
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
