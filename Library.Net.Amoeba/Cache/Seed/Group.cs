using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Group", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Group : ItemBase<Group>, IGroup<Key>, ICloneable<Group>, IThisLock
    {
        private enum SerializeId : byte
        {
            Key = 0,

            CorrectionAlgorithm = 1,
            InformationLength = 2,
            BlockLength = 3,
            Length = 4,
        }

        private KeyCollection _keys;

        private CorrectionAlgorithm _correctionAlgorithm = 0;
        private int _informationLength;
        private int _blockLength;
        private long _length;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public Group()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            lock (this.ThisLock)
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
                        if (id == (byte)SerializeId.Key)
                        {
                            this.Keys.Add(Key.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.CorrectionAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CorrectionAlgorithm = (CorrectionAlgorithm)Enum.Parse(typeof(CorrectionAlgorithm), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.InformationLength)
                        {
                            byte[] buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buffer, 0, 4);

                            this.InformationLength = NetworkConverter.ToInt32(buffer);

                            bufferManager.ReturnBuffer(buffer);
                        }
                        else if (id == (byte)SerializeId.BlockLength)
                        {
                            byte[] buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buffer, 0, 4);

                            this.BlockLength = NetworkConverter.ToInt32(buffer);

                            bufferManager.ReturnBuffer(buffer);
                        }
                        else if (id == (byte)SerializeId.Length)
                        {
                            byte[] buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                            rangeStream.Read(buffer, 0, 8);

                            this.Length = NetworkConverter.ToInt64(buffer);

                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Keys
                foreach (var k in this.Keys)
                {
                    Stream exportStream = k.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                // CorrectionAlgorithm
                if (this.CorrectionAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.CorrectionAlgorithm);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CorrectionAlgorithm);

                    streams.Add(bufferStream);
                }
                // InformationLength
                if (this.InformationLength != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.InformationLength);
                    bufferStream.Write(NetworkConverter.GetBytes(this.InformationLength), 0, 4);

                    streams.Add(bufferStream);
                }
                // BlockLength
                if (this.BlockLength != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)4), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.BlockLength);
                    bufferStream.Write(NetworkConverter.GetBytes(this.BlockLength), 0, 4);

                    streams.Add(bufferStream);
                }
                // Length
                if (this.Length != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)8), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Length);
                    bufferStream.Write(NetworkConverter.GetBytes(this.Length), 0, 8);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Keys.Count == 0) return 0;
                else return this.Keys[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Group)) return false;

            return this.Equals((Group)obj);
        }

        public override bool Equals(Group other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Keys == null) != (other.Keys == null)

                || this.CorrectionAlgorithm != other.CorrectionAlgorithm
                || this.InformationLength != other.InformationLength
                || this.BlockLength != other.BlockLength
                || this.Length != other.Length)
            {
                return false;
            }

            if (this.Keys != null && other.Keys != null)
            {
                if (!Collection.Equals(this.Keys, other.Keys)) return false;
            }

            return true;
        }

        #region IGroup<Key>

        IList<Key> IGroup<Key>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keys;
                }
            }
        }

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keys == null)
                        _keys = new KeyCollection();

                    return _keys;
                }
            }
        }

        #endregion

        #region ICorrectionAlgorithm

        [DataMember(Name = "CorrectionAlgorithm")]
        public CorrectionAlgorithm CorrectionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _correctionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CorrectionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _correctionAlgorithm = value;
                    }
                }
            }
        }

        [DataMember(Name = "InformationLength")]
        public int InformationLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _informationLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _informationLength = value;
                }
            }
        }

        [DataMember(Name = "BlockLength")]
        public int BlockLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _blockLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _blockLength = value;
                }
            }
        }

        [DataMember(Name = "Length")]
        public long Length
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _length;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _length = value;
                }
            }
        }

        #endregion

        #region ICloneable<Group>

        public Group Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Group.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #endregion
    }
}
