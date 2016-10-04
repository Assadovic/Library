using System;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "BackgroundDownloadState")]
    enum BackgroundDownloadState
    {
        [EnumMember(Value = "Downloading")]
        Downloading = 0,

        [EnumMember(Value = "Decoding")]
        Decoding = 1,

        [EnumMember(Value = "Completed")]
        Completed = 2,

        [EnumMember(Value = "Error")]
        Error = 3,
    }

    [DataContract(Name = "BackgroundDownloadItem")]
    sealed class BackgroundDownloadItem
    {
        private BackgroundDownloadState _state;

        private Metadata _metadata;

        private int _depth;
        private Index _index;
        private Stream _stream;

        private IndexCollection _indexes;

        private DateTime _updateTime;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        private object ThisLock
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

        [DataMember(Name = "State")]
        public BackgroundDownloadState State
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

        [DataMember(Name = "Metadata")]
        public Metadata Metadata
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _metadata;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _metadata = value;
                }
            }
        }

        [DataMember(Name = "Depth")]
        public int Depth
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _depth;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _depth = value;
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

        [DataMember(Name = "Stream")]
        public Stream Stream
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stream;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _stream = value;
                }
            }
        }

        [DataMember(Name = "Indexes")]
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

        [DataMember(Name = "UpdateTime")]
        public DateTime UpdateTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _updateTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _updateTime = value;
                }
            }
        }
    }
}
