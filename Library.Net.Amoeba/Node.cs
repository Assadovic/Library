﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Utilities;

namespace Library.Net.Amoeba
{
    /// <summary>
    /// ノードに関する情報を表します
    /// </summary>
    [DataContract(Name = "Node", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Node : ItemBase<Node>, INode
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Uri = 1,
        }

        private volatile byte[] _id;
        private volatile UriCollection _uris;

        private volatile int _hashCode;

        public static readonly int MaxIdLength = 32;
        public static readonly int MaxUriCount = 32;

        public Node(byte[] id, IEnumerable<string> uris)
        {
            this.Id = id;
            if (uris != null) this.ProtectedUris.AddRange(uris);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            for (;;)
            {
                byte id;

                using (var rangeStream = ItemUtilities.GetStream(out id, stream))
                {
                    if (rangeStream == null) return;

                    if (id == (byte)SerializeId.Id)
                    {
                        this.Id = ItemUtilities.GetByteArray(rangeStream);
                    }

                    else if (id == (byte)SerializeId.Uri)
                    {
                        this.ProtectedUris.Add(ItemUtilities.GetString(rangeStream));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Id
            if (this.Id != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Id, this.Id);
            }

            // Uris
            foreach (var value in this.Uris)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Uri, value);
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
            if ((object)obj == null || !(obj is Node)) return false;

            return this.Equals((Node)obj);
        }

        public override bool Equals(Node other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.Id == null) != (other.Id == null)
                || (this.Uris == null) != (other.Uris == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Unsafe.Equals(this.Id, other.Id)) return false;
            }

            if (this.Uris != null && other.Uris != null)
            {
                if (!CollectionUtilities.Equals(this.Uris, other.Uris)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return String.Join(", ", this.Uris);
        }

        #region INode

        /// <summary>
        /// Idを取得または設定します
        /// </summary>
        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                return _id;
            }
            private set
            {
                if (value != null && value.Length > Node.MaxIdLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _id = value;
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

        #endregion

        private volatile ReadOnlyCollection<string> _readOnlyUris;

        public IEnumerable<string> Uris
        {
            get
            {
                if (_readOnlyUris == null)
                    _readOnlyUris = new ReadOnlyCollection<string>(this.ProtectedUris.ToArray());

                return _readOnlyUris;
            }
        }

        [DataMember(Name = "Uris")]
        private UriCollection ProtectedUris
        {
            get
            {
                if (_uris == null)
                    _uris = new UriCollection(Node.MaxUriCount);

                return _uris;
            }
        }
    }
}
