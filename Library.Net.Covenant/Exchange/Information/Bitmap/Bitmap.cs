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
    public sealed class Bitmap : ItemBase<Bitmap>, IBitmap
    {
        private enum SerializeId : byte
        {
            Value = 0,
        }

        private byte[] _value;

        private volatile int _hashCode;

        public static readonly int MaxLength = 32 * 1024;

        public Bitmap(int length)
        {
            this.Value = new byte[(length + (8 - 1)) / 8];
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                byte id;
                {
                    byte[] idBuffer = new byte[1];
                    if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                    id = idBuffer[0];
                }

                int length;
                {
                    byte[] lengthBuffer = new byte[4];
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    length = NetworkConverter.ToInt32(lengthBuffer);
                }

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    this.Value = ItemUtilities.GetByteArray(rangeStream);
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Value
            if (this.Value != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Value, this.Value);
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
            if ((object)obj == null || !(obj is Bitmap)) return false;

            return this.Equals((Bitmap)obj);
        }

        public override bool Equals(Bitmap other)
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

        public bool Get(int index)
        {
            if (index < 0 || index >= this.Length) throw new ArgumentOutOfRangeException(nameof(index));

            return ((_value[index / 8] << (index % 8)) & 0x80) == 0x80;
        }

        public void Set(int index, bool flag)
        {
            if (index < 0 || index >= this.Length) throw new ArgumentOutOfRangeException(nameof(index));

            if (flag)
            {
                _value[index / 8] |= (byte)(0x80 >> (index % 8));
            }
            else
            {
                _value[index / 8] &= (byte)(~(0x80 >> (index % 8)));
            }
        }

        public int Length
        {
            get
            {
                return _value.Length * 8;
            }
        }

        #endregion
    }
}
