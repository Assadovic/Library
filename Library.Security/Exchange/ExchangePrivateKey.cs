﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Utilities;

namespace Library.Security
{
    [DataContract(Name = "ExchangePrivateKey")]
    public sealed class ExchangePrivateKey : ItemBase<ExchangePrivateKey>, IExchangeDecrypt
    {
        private enum SerializeId
        {
            CreationTime = 0,
            ExchangeAlgorithm = 1,
            PrivateKey = 2,
        }

        private DateTime _creationTime;
        private volatile ExchangeAlgorithm _exchangeAlgorithm;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;

        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public ExchangePrivateKey(Exchange exchange)
        {
            this.CreationTime = exchange.CreationTime;
            this.ExchangeAlgorithm = exchange.ExchangeAlgorithm;
            this.PrivateKey = exchange.PrivateKey;
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
            if ((object)obj == null || !(obj is ExchangePrivateKey)) return false;

            return this.Equals((ExchangePrivateKey)obj);
        }

        public override bool Equals(ExchangePrivateKey other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CreationTime != other.CreationTime
                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Unsafe.Equals(this.PrivateKey, other.PrivateKey)) return false;
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

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && value.Length > Exchange.MaxPublickeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _privateKey = value;
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
