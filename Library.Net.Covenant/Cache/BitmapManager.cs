using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    public sealed class BitmapManager
    {
        private byte[] _value;

        public static readonly int MaxLength = 32 * 1024;

        public BitmapManager(int length)
        {
            this.Value = new byte[(length + (8 - 1)) / 8];
        }

        public BitmapManager(byte[] value)
        {
            this.Value = value;
        }

        #region IBitmap

        private byte[] Value
        {
            get
            {
                return _value;
            }
            set
            {
                if (value != null && value.Length > (BitmapManager.MaxLength / 8))
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

        public BitmapManager And(BitmapManager target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.And(this.Value, target.Value, buffer);

            return new BitmapManager(buffer);
        }

        public BitmapManager Or(BitmapManager target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.Or(this.Value, target.Value, buffer);

            return new BitmapManager(buffer);
        }

        public BitmapManager Xor(BitmapManager target)
        {
            var buffer = new byte[Math.Max(this.Value.Length, target.Value.Length)];
            Unsafe.Xor(this.Value, target.Value, buffer);

            return new BitmapManager(buffer);
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
