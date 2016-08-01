using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Library.Io;
using Library.Utilities;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Tag", Namespace = "http://Library/Net/Outopos")]
    public class Tag : ItemBase<Tag>, ITag
    {
        private enum SerializeId
        {
            Name = 0,
            Id = 1,
        }

        private static Intern<string> _nameCache = new Intern<string>();
        private volatile string _name;
        private static Intern<byte[]> _idCache = new Intern<byte[]>(new ByteArrayEqualityComparer());
        private volatile byte[] _id;

        private volatile int _hashCode;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxIdLength = 32;

        public Tag(string name, byte[] id)
        {
            this.Name = name;
            this.Id = id;
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

                    if (type == (int)SerializeId.Name)
                    {
                        this.Name = ItemUtilities.GetString(rangeStream);
                    }
                    else if (type == (int)SerializeId.Id)
                    {
                        this.Id = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Name
            if (this.Name != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Name, this.Name);
            }
            // Id
            if (this.Id != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Id, this.Id);
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
            if ((object)obj == null || !(obj is Tag)) return false;

            return this.Equals((Tag)obj);
        }

        public override bool Equals(Tag other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Name != other.Name
                || (this.Id == null) != (other.Id == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Unsafe.Equals(this.Id, other.Id)) return false;
            }

            return true;
        }

        #region ITag

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            private set
            {
                if (value != null && value.Length > Tag.MaxNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _name = _nameCache.GetValue(value, this);
                }
            }
        }

        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                return _id;
            }
            private set
            {
                if (value != null && (value.Length > Tag.MaxIdLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _id = _idCache.GetValue(value, this);
                }

                if (value != null)
                {
                    _hashCode = RuntimeHelpers.GetHashCode(_id);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}
