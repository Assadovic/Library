﻿using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "DownloadState", Namespace = "http://Library/Net/Amoeba")]
    public enum DownloadState
    {
        [EnumMember(Value = "Downloading")]
        Downloading = 0,

        [EnumMember(Value = "ParityDecoding")]
        ParityDecoding = 1,

        [EnumMember(Value = "Decoding")]
        Decoding = 2,

        [EnumMember(Value = "Completed")]
        Completed = 3,

        [EnumMember(Value = "Error")]
        Error = 4,
    }

    [DataContract(Name = "DownloadItem", Namespace = "http://Library/Net/Amoeba")]
    sealed class DownloadItem : IDeepCloneable<DownloadItem>, IThisLock
    {
        private DownloadState _state;
        private int _priority = 3;

        private Seed _seed;

        private int _rank;
        private Index _index;
        private string _path;

        private long _decodeBytes;
        private long _decodingBytes;

        private IndexCollection _indexes;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        [DataMember(Name = "State")]
        public DownloadState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _state = value;
                }
            }
        }

        [DataMember(Name = "Priority")]
        public int Priority
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _priority;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _priority = value;
                }
            }
        }

        [DataMember(Name = "Seed")]
        public Seed Seed
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _seed;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _seed = value;
                }
            }
        }

        [DataMember(Name = "Rank")]
        public int Rank
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _rank;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _rank = value;
                }
            }
        }

        [DataMember(Name = "Index")]
        public Index Index
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _index;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _index = value;
                }
            }
        }

        [DataMember(Name = "Path")]
        public string Path
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _path;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _path = value;
                }
            }
        }

        [DataMember(Name = "DecodeBytes")]
        public long DecodeBytes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _decodeBytes;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _decodeBytes = value;
                }
            }
        }

        [DataMember(Name = "DecodingBytes")]
        public long DecodingBytes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _decodingBytes;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _decodingBytes = value;
                }
            }
        }

        [DataMember(Name = "Indexs")]
        public IndexCollection Indexes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_indexes == null)
                        _indexes = new IndexCollection();

                    return _indexes;
                }
            }
        }

        public DownloadItem DeepClone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(DownloadItem));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (DownloadItem)ds.ReadObject(textDictionaryReader);
                    }
                }
            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                lock (_thisStaticLock)
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
