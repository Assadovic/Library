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

    enum BackgroundItemType
    {
        None,
        Link,
        Store,
    }

    interface IBackgroundDownloadItem
    {
        BackgroundItemType Type { get; }

        BackgroundDownloadState State { get; set; }

        Seed Seed { get; set; }

        int Depth { get; set; }
        Index Index { get; set; }
        object Value { get; set; }

        IndexCollection Indexes { get; }
    }

    [DataContract(Name = "BackgroundDownloadItem")]
    sealed class BackgroundDownloadItem<T> : IBackgroundDownloadItem
    {
        private BackgroundDownloadState _state;

        private Seed _seed;

        private int _rank;
        private Index _index;
        private T _value;

        private IndexCollection _indexes;

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

        BackgroundItemType IBackgroundDownloadItem.Type
        {
            get
            {
                if (typeof(T) == typeof(Link)) return BackgroundItemType.Link;
                else if (typeof(T) == typeof(Store)) return BackgroundItemType.Store;

                return BackgroundItemType.None;
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

        [DataMember(Name = "Depth")]
        public int Depth
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

        [DataMember(Name = "Value")]
        public T Value
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _value = value;
                }
            }
        }

        object IBackgroundDownloadItem.Value
        {
            get
            {
                return this.Value;
            }
            set
            {
                this.Value = (T)value;
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
    }
}
