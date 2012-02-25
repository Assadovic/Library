﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Collections;
using Library.Net.Amoeba;
using Library;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Box", Namespace = "http://Library/Net/Amoeba")]
    public class Box : CertificateItemBase<Box>, IBox, IThisLock
    {
        private enum SerializeId : byte
        {
            Name = 0,
            CreationTime = 1,
            Comment = 2,
            Seed = 3,
            Box = 4,

            Certificate = 5,
        }

        private string _name = null;
        private DateTime _creationTime = DateTime.MinValue;
        private string _comment = null;
        private SeedCollection _seeds = null;
        private BoxCollection _boxes = null;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxNameLength = 256;
        public const int MaxCommentLength = 1024;

        public Box()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                Encoding encoding = new UTF8Encoding(false);
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Name)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Name = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }
                        else if (id == (int)SerializeId.Comment)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Comment = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.Seed)
                        {
                            this.Seeds.Add(Seed.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Box)
                        {
                            this.Boxes.Add(Box.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Name
                if (this.Name != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Name);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Name);

                    streams.Add(bufferStream);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CreationTime);

                    streams.Add(bufferStream);
                }
                // Comment
                if (this.Comment != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Comment);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Comment);

                    streams.Add(bufferStream);
                }
                // Seeds
                foreach (var s in this.Seeds)
                {
                    Stream exportStream = s.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Seed);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }
                // Boxes
                foreach (var b in this.Boxes)
                {
                    Stream exportStream = b.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Box);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                // Certificate
                if (this.Certificate != null)
                {
                    Stream exportStream = this.Certificate.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Certificate);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                return new AddStream(streams);
            }
        }

        public override int GetHashCode()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.CreationTime.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Box)) return false;

            return this.Equals((Box)obj);
        }

        public override bool Equals(Box other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Name != other.Name
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment
                || ((this.Seeds == null) != (other.Seeds == null))
                || ((this.Boxes == null) != (other.Boxes == null))

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Seeds != null && other.Seeds != null)
            {
                if (!Collection.Equals(this.Seeds, other.Seeds)) return false;
            }

            if (this.Boxes != null && other.Boxes != null)
            {
                if (!Collection.Equals(this.Boxes, other.Boxes)) return false;

            }

            return true;
        }

        public override string ToString()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.Name;
            }
        }

        public override Box DeepClone()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Box.Import(stream, bufferManager);
                }
            }
        }

        protected override Stream GetCertificateStream()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    using (BufferManager bufferManager = new BufferManager())
                    {
                        return this.Export(bufferManager);
                    }
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        #region IDirectory<Keyword> メンバ

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _name;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (value != null && value.Length > Box.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _creationTime;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _comment;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (value != null && value.Length > Box.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        [DataMember(Name = "Seeds")]
        public SeedCollection Seeds
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_seeds == null)
                        _seeds = new SeedCollection();

                    return _seeds;
                }
            }
        }

        [DataMember(Name = "Boxes")]
        public BoxCollection Boxes
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_boxes == null)
                        _boxes = new BoxCollection();

                    return _boxes;
                }
            }
        }

        #endregion

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null) _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
