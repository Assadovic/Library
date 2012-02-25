﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using Library;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Index", Namespace = "http://Library/Net/Amoeba")]
    public class Index : ItemBase<Index>, IIndex<Group, Key>, IThisLock
    {
        private enum SerializeId : byte
        {
            Group = 0,

            CompressionAlgorithm = 1,

            CryptoAlgorithm = 2,
            CryptoKey = 3,
        }

        private GroupCollection _groups = null;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey = null;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxCryptoKeyLength = 64;

        public Index()
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
                        if (id == (byte)SerializeId.Group)
                        {
                            this.Groups.Add(Group.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.CompressionAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), reader.ReadToEnd());
                            }
                        }

                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.CryptoKey)
                        {
                            byte[] buffer = new byte[(int)rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.CryptoKey = buffer;
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

                // Groups
                foreach (var g in this.Groups)
                {
                    Stream exportStream = g.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Group);

                    streams.Add(new AddStream(bufferStream, exportStream));
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.CompressionAlgorithm);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CompressionAlgorithm);

                    streams.Add(bufferStream);
                }

                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.CryptoAlgorithm);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CryptoAlgorithm);

                    streams.Add(bufferStream);
                }
                // CryptoKey
                if (this.CryptoKey != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.CryptoKey.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CryptoKey);
                    bufferStream.Write(this.CryptoKey, 0, this.CryptoKey.Length);

                    streams.Add(bufferStream);
                }

                return new AddStream(streams);
            }
        }

        public override int GetHashCode()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (this.Groups == null) return 0;
                else if (this.Groups.Count == 0) return 0;
                else if (this.Groups[0].Keys == null) return 0;
                else if (this.Groups[0].Keys.Count == 0) return 0;
                else return this.Groups[0].Keys[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Index)) return false;

            return this.Equals((Index)obj);
        }

        public override bool Equals(Index other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (((this.Groups == null) != (other.Groups == null))

                || (this.CompressionAlgorithm != other.CompressionAlgorithm)

                || (this.CryptoAlgorithm != other.CryptoAlgorithm)
                || ((this.CryptoKey == null) != (other.CryptoKey == null)))
            {
                return false;
            }

            if (this.Groups != null && other.Groups != null)
            {
                if (!Collection.Equals(this.Groups, other.Groups)) return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Collection.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        public override Index DeepClone()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Index.Import(stream, bufferManager);
                }
            }
        }

        #region IIndex<Group, Header> メンバ

        IList<Group> IIndex<Group, Key>.Groups
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return this.Groups;
                }
            }
        }

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
                }
            }
        }

        #endregion

        #region ICompressionAlgorithm メンバ

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (!Enum.IsDefined(typeof(CompressionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _compressionAlgorithm = value;
                    }
                }
            }
        }

        #endregion

        #region ICryptoAlgorithm メンバ

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (!Enum.IsDefined(typeof(CryptoAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoAlgorithm = value;
                    }
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cryptoKey;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (value != null && value.Length > Index.MaxCryptoKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoKey = value;
                    }
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
                    if (_thisLock == null) 
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}
