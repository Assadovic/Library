using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "BackgroundUploadState")]
    enum BackgroundUploadState
    {
        [EnumMember(Value = "Encoding")]
        Encoding = 0,

        [EnumMember(Value = "Uploading")]
        Uploading = 1,

        [EnumMember(Value = "Completed")]
        Completed = 2,

        [EnumMember(Value = "Error")]
        Error = 3,
    }

    [DataContract(Name = "BackgroundUploadItem")]
    sealed class BackgroundUploadItem
    {
        private BackgroundUploadState _state;

        private Link _link;
        private Profile _profile;
        private Store _store;
        private Message _message;

        private string _scheme;
        private string _type;
        private string _signature;
        private Tag _tag;
        private DateTime _creationTime;

        private int _depth;
        private KeyCollection _keys;
        private GroupCollection _groups;
        private int _blockLength;
        private CorrectionAlgorithm _correctionAlgorithm;
        private HashAlgorithm _hashAlgorithm;
        private int _miningLimit;
        private TimeSpan _miningTime;
        private ExchangePublicKey _exchangePublicKey;
        private DigitalSignature _digitalSignature;

        private List<Key> _LockedKeys;
        private HashSet<Key> _uploadKeys;

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
        public BackgroundUploadState State
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

        [DataMember(Name = "Link")]
        public Link Link
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _link;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _link = value;
                }
            }
        }

        [DataMember(Name = "Profile")]
        public Profile Profile
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _profile;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _profile = value;
                }
            }
        }

        [DataMember(Name = "Store")]
        public Store Store
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _store;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _store = value;
                }
            }
        }

        [DataMember(Name = "Message")]
        public Message Message
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _message;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _message = value;
                }
            }
        }

        [DataMember(Name = "Scheme")]
        public string Scheme
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _scheme;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _scheme = value;
                }
            }
        }

        [DataMember(Name = "Type")]
        public string Type
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

        [DataMember(Name = "Signature")]
        public string Signature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _signature;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _signature = value;
                }
            }
        }

        [DataMember(Name = "Tag")]
        public Tag Tag
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _tag;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _creationTime = value;
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

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
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
                    _correctionAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _hashAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "MiningLimit")]
        public int MiningLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _miningLimit;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _miningLimit = value;
                }
            }
        }

        [DataMember(Name = "MiningTime")]
        public TimeSpan MiningTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _miningTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _miningTime = value;
                }
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }

            set
            {
                _exchangePublicKey = value;
            }
        }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _digitalSignature;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _digitalSignature = value;
                }
            }
        }

        [DataMember(Name = "LockedKeys")]
        public List<Key> LockedKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_LockedKeys == null)
                        _LockedKeys = new List<Key>();

                    return _LockedKeys;
                }
            }
        }

        [DataMember(Name = "UploadKeys")]
        public HashSet<Key> UploadKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_uploadKeys == null)
                        _uploadKeys = new HashSet<Key>();

                    return _uploadKeys;
                }
            }
        }
    }
}
