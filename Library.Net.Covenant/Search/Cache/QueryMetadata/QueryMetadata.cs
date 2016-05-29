using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Library.Io;

namespace Library.Net.Covenant
{
    [DataContract(Name = "QueryMetadata", Namespace = "http://Library/Net/Covenant")]
    sealed class QueryMetadata : ItemBase<QueryMetadata>, IQueryMetadata
    {
        private enum SerializeId : byte
        {
            Type = 0,
            CreationTime = 1,
            Signature = 2,
        }

        private MetadataType _type;
        private DateTime _creationTime;
        private volatile string _signature;

        internal QueryMetadata(MetadataType type, DateTime creationTime, string signature)
        {
            this.Type = type;
            this.CreationTime = creationTime;
            this.Signature = signature;
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
                    if (id == (byte)SerializeId.Type)
                    {
                        this.Type = (MetadataType)Enum.Parse(typeof(MetadataType), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.CreationTime)
                    {
                        this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                    }
                    else if (id == (byte)SerializeId.Signature)
                    {
                        this.Signature = ItemUtilities.GetString(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            var bufferStream = new BufferStream(bufferManager);

            // Type
            if (this.Type != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Type, this.Type.ToString());
            }
            // CreationTime
            if (this.CreationTime != DateTime.MinValue)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
            }
            // Signature
            if (this.Signature != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Signature, this.Signature);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return this.Signature.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is QueryMetadata)) return false;

            return this.Equals((QueryMetadata)obj);
        }

        public override bool Equals(QueryMetadata other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Type != other.Type
                || this.CreationTime != other.CreationTime
                || this.Signature != other.Signature)
            {
                return false;
            }

            return true;
        }

        #region IQueryMetadata

        [DataMember(Name = "Type")]
        public MetadataType Type
        {
            get
            {
                return _type;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(MetadataType), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                return _creationTime;
            }
            private set
            {
                var utc = value.ToUniversalTime();
                _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
            }
        }

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                if (value != null && !Library.Security.Signature.Check(value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _signature = value;
                }
            }
        }

        #endregion
    }
}
