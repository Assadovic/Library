﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Utilities;

namespace Library.Security
{
    [DataContract(Name = "Cash", Namespace = "http://Library/Security")]
    public sealed class Cash : ItemBase<Cash>
    {
        private enum SerializeId
        {
            CashAlgorithm = 0,
            Key = 1,
        }

        private volatile CashAlgorithm _cashAlgorithm = 0;
        private volatile byte[] _key;

        private volatile int _hashCode;

        public static readonly int MaxKeyLength = 32;
        public static readonly int MaxValueLength = 32;

        internal Cash(CashAlgorithm cashAlgorithm, byte[] key)
        {
            this.CashAlgorithm = cashAlgorithm;
            this.Key = key;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                int type;

                using (var rangeStream = ItemUtilities.GetStream(out type, stream))
                {
                    if (rangeStream == null) return;

                    if (type == (int)SerializeId.CashAlgorithm)
                    {
                        this.CashAlgorithm = (CashAlgorithm)Enum.Parse(typeof(CashAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                    else if (type == (int)SerializeId.Key)
                    {
                        this.Key = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // CashAlgorithm
            if (this.CashAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (int)SerializeId.CashAlgorithm, this.CashAlgorithm.ToString());
            }
            // Key
            if (this.Key != null)
            {
                ItemUtilities.Write(bufferStream, (int)SerializeId.Key, this.Key);
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
            if ((object)obj == null || !(obj is Cash)) return false;

            return this.Equals((Cash)obj);
        }

        public override bool Equals(Cash other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CashAlgorithm != other.CashAlgorithm
                || ((this.Key == null) != (other.Key == null)))
            {
                return false;
            }

            if (this.Key != null && other.Key != null)
            {
                if (!Unsafe.Equals(this.Key, other.Key)) return false;
            }

            return true;
        }

        [DataMember(Name = "CashAlgorithm")]
        public CashAlgorithm CashAlgorithm
        {
            get
            {
                return _cashAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(CashAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cashAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "Key")]
        public byte[] Key
        {
            get
            {
                return _key;
            }
            private set
            {
                if (value != null && value.Length > Cash.MaxKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _key = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }
    }
}
