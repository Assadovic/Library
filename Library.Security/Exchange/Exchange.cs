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
    [DataContract(Name = "Exchange")]
    public sealed class Exchange : ItemBase<Exchange>, IExchangeEncrypt, IExchangeDecrypt
    {
        private enum SerializeId
        {
            CreationTime = 0,
            ExchangeAlgorithm = 1,
            PublicKey = 2,
            PrivateKey = 3,
        }

        private DateTime _creationTime;
        private volatile ExchangeAlgorithm _exchangeAlgorithm;
        private volatile byte[] _publicKey;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;

        public static readonly int MaxPublickeyLength = 1024 * 8;
        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public Exchange(ExchangeAlgorithm exchangeAlgorithm)
        {
            this.CreationTime = DateTime.UtcNow;
            this.ExchangeAlgorithm = exchangeAlgorithm;

            if (exchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                byte[] publicKey, privateKey;

                Rsa2048.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
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
                    else if (id == (int)SerializeId.PrivateKey)
                    {
                        this.PrivateKey = reader.GetBytes();
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
                // PrivateKey
                if (this.PrivateKey != null)
                {
                    writer.Write((int)SerializeId.PrivateKey, this.PrivateKey);
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
            if ((object)obj == null || !(obj is Exchange)) return false;

            return this.Equals((Exchange)obj);
        }

        public override bool Equals(Exchange other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Unsafe.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Unsafe.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        public ExchangePublicKey GetExchangePublicKey()
        {
            return new ExchangePublicKey(this);
        }

        public ExchangePrivateKey GetExchangePrivateKey()
        {
            return new ExchangePrivateKey(this);
        }

        public static byte[] Encrypt(IExchangeEncrypt exchangeEncrypt, byte[] value)
        {
            if (exchangeEncrypt.ExchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                return Rsa2048.Encrypt(exchangeEncrypt.PublicKey, value);
            }

            return null;
        }

        public static byte[] Decrypt(IExchangeDecrypt exchangeDecrypt, byte[] value)
        {
            if (exchangeDecrypt.ExchangeAlgorithm == ExchangeAlgorithm.Rsa2048)
            {
                return Rsa2048.Decrypt(exchangeDecrypt.PrivateKey, value);
            }

            return null;
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

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && value.Length > Exchange.MaxPrivatekeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _privateKey = value;
                }
            }
        }
    }
}
