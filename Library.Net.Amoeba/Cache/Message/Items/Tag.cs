﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Library.Io;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Tag")]
    public sealed class Tag : ItemBase<Tag>, ITag
    {
        private enum SerializeId
        {
            Name = 0,
            Id = 1,
        }

        private volatile string _name;
        private volatile byte[] _id;

        private volatile int _hashCode;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxIdLength = 32;

        public Tag(string name, byte[] id)
        {
            this.Name = name;
            this.Id = id;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.Name)
                    {
                        this.Name = reader.GetString();
                    }
                    else if (id == (int)SerializeId.Id)
                    {
                        this.Id = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Name
                if (this.Name != null)
                {
                    writer.Write((int)SerializeId.Name, this.Name);
                }
                // Id
                if (this.Id != null)
                {
                    writer.Write((int)SerializeId.Id, this.Id);
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
                    _name = value;
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
                    _id = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtils.GetHashCode(_id);
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
