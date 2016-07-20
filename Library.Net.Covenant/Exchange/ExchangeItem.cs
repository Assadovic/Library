using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "ExchangeType", Namespace = "http://Library/Net/Covenant")]
    public enum ExchangeType
    {
        [EnumMember(Value = "Download")]
        Download = 0,

        [EnumMember(Value = "Upload")]
        Upload = 1,
    }

    [DataContract(Name = "ExchangeState", Namespace = "http://Library/Net/Covenant")]
    public enum ExchangeState
    {
        [EnumMember(Value = "ComputeHash")]
        ComputeHash = 0,

        [EnumMember(Value = "Exchanging")]
        Exchanging = 1,

        [EnumMember(Value = "Completed")]
        Completed = 2,

        [EnumMember(Value = "Error")]
        Error = 3,
    }

    [DataContract(Name = "ExchangeItem", Namespace = "http://Library/Net/Covenant")]
    sealed class ExchangeItem
    {
        private ExchangeType _type;
        private ExchangeState _state;

        private Seed _seed;
        private string _path;

        private long _streamOffset;
        private long _streamLength;

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

        [DataMember(Name = "Type")]
        public ExchangeType Type
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _type;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "State")]
        public ExchangeState State
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

        [DataMember(Name = "StreamOffset")]
        public long StreamOffset
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _streamOffset;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _streamOffset = value;
                }
            }
        }

        [DataMember(Name = "StreamLength")]
        public long StreamLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _streamLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _streamLength = value;
                }
            }
        }
    }
}
