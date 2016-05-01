using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    [DataContract(Name = "Location", Namespace = "http://Library/Net/Covenant")]
    sealed class Location : ItemBase<Location>, ILocation
    {
        private enum SerializeId : byte
        {
            Key = 0,
            Uri = 1,
        }

        private volatile Key _key;
        private volatile UriCollection _uris;

        public static readonly int MaxUriCount = 32;

        internal Location(Key key, IEnumerable<string> uris)
        {
            this.Key = key;
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
                    if (id == (byte)SerializeId.Key)
                    {
                        this.Key = Key.Import(rangeStream, bufferManager);
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

            // Key
            if (this.Key != null)
            {
                using (var stream = this.Key.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                }
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
            return this.Key.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Location)) return false;

            return this.Equals((Location)obj);
        }

        public override bool Equals(Location other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Key != other.Key
                || (this.Uris == null) != (other.Uris == null))
            {
                return false;
            }

            if (this.Uris != null && other.Uris != null)
            {
                if (!CollectionUtilities.Equals(this.Uris, other.Uris)) return false;
            }

            return true;
        }

        #region ILocation

        [DataMember(Name = "Key")]
        public Key Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }

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
                    _uris = new UriCollection(Location.MaxUriCount);

                return _uris;
            }
        }

        #endregion

        #region IComputeHash

        private volatile byte[] _sha256_hash;

        public byte[] CreateHash(HashAlgorithm hashAlgorithm)
        {
            if (_sha256_hash == null)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    _sha256_hash = Sha256.ComputeHash(stream);
                }
            }

            if (hashAlgorithm == HashAlgorithm.Sha256)
            {
                return _sha256_hash;
            }

            return null;
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            return Unsafe.Equals(this.CreateHash(hashAlgorithm), hash);
        }

        #endregion
    }
}
