using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    [DataContract(Name = "Bitmap", Namespace = "http://Library/Net/Covenant")]
    public sealed class Bitmap : IBitmap<Bitmap>, IEquatable<Bitmap>
    {
        private byte[] _value;

        public static readonly int MaxLength = 32 * 1024;

        public Bitmap(int length)
        {
            this.Value = new byte[(length + (8 - 1)) / 8];
        }

        public Bitmap(byte[] value)
        {
            this.Value = value;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Bitmap)) return false;

            return this.Equals((Bitmap)obj);
        }

        public bool Equals(Bitmap other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.Value == null) != (other.Value == null))
            {
                return false;
            }

            if (this.Value != null && other.Value != null)
            {
                if (!Unsafe.Equals(this.Value, other.Value)) return false;
            }

            return true;
        }

        #region IBitmap

        [DataMember(Name = "Value")]
        private byte[] Value
        {
            get
            {
                return _value;
            }
            set
            {
                if (value != null && value.Length > (Bitmap.MaxLength / 8))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _value = value;
                }
            }
        }

        public bool Get(int index)
        {
            if (index < 0 || index >= this.Length) throw new ArgumentOutOfRangeException(nameof(index));

            return ((this.Value[index / 8] << (index % 8)) & 0x80) == 0x80;
        }

        public void Set(int index, bool flag)
        {
            if (index < 0 || index >= this.Length) throw new ArgumentOutOfRangeException(nameof(index));

            if (flag)
            {
                this.Value[index / 8] |= (byte)(0x80 >> (index % 8));
            }
            else
            {
                this.Value[index / 8] &= (byte)(~(0x80 >> (index % 8)));
            }
        }

        public int Length
        {
            get
            {
                return this.Value.Length * 8;
            }
        }

        public Bitmap And(Bitmap target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.And(this.Value, target.Value, buffer);

            return new Bitmap(buffer);
        }

        public Bitmap Or(Bitmap target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.Or(this.Value, target.Value, buffer);

            return new Bitmap(buffer);
        }

        public Bitmap Xor(Bitmap target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.Xor(this.Value, target.Value, buffer);

            return new Bitmap(buffer);
        }

        public byte[] ToBinary()
        {
            var buffer = new byte[this.Value.Length];
            Unsafe.Copy(this.Value, 0, buffer, 0, buffer.Length);

            return buffer;
        }

        #endregion
    }
}
