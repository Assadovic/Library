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

    interface IBackgroundUploadItem
    {
        BackgroundItemType Type { get; }

        BackgroundUploadState State { get; set; }

        object Value { get; set; }

        string Name { get; set; }
        long Length { get; set; }
        DateTime CreationTime { get; set; }
        int Depth { get; set; }
        KeyCollection Keys { get; }
        GroupCollection Groups { get; }
        int BlockLength { get; set; }
        CompressionAlgorithm CompressionAlgorithm { get; set; }
        CryptoAlgorithm CryptoAlgorithm { get; set; }
        byte[] CryptoKey { get; set; }
        CorrectionAlgorithm CorrectionAlgorithm { get; set; }
        HashAlgorithm HashAlgorithm { get; set; }
        DigitalSignature DigitalSignature { get; set; }
        Seed Seed { get; set; }

        List<Key> LockedKeys { get; }
        HashSet<Key> UploadKeys { get; }
        HashSet<Key> UploadedKeys { get; }
    }

    [DataContract(Name = "BackgroundUploadItem")]
    sealed class BackgroundUploadItem<T> : IBackgroundUploadItem
    {
        private BackgroundUploadState _state;

        private T _value;

        private string _name;
        private long _length;
        private DateTime _creationTime;
        private int _depth;
        private KeyCollection _keys;
        private GroupCollection _groups;
        private int _blockLength;
        private CompressionAlgorithm _compressionAlgorithm;
        private CryptoAlgorithm _cryptoAlgorithm;
        private byte[] _cryptoKey;
        private CorrectionAlgorithm _correctionAlgorithm;
        private HashAlgorithm _hashAlgorithm;
        private DigitalSignature _digitalSignature;
        private Seed _seed;

        private List<Key> _LockedKeys;
        private HashSet<Key> _uploadKeys;
        private HashSet<Key> _uploadedKeys;

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

        BackgroundItemType IBackgroundUploadItem.Type
        {
            get
            {
                if (typeof(T) == typeof(Link)) return BackgroundItemType.Link;
                else if (typeof(T) == typeof(Store)) return BackgroundItemType.Store;

                return BackgroundItemType.None;
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

        object IBackgroundUploadItem.Value
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

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _name = value;
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

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _compressionAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _cryptoKey = value;
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

        [DataMember(Name = "UploadedKeys")]
        public HashSet<Key> UploadedKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_uploadedKeys == null)
                        _uploadedKeys = new HashSet<Key>();

                    return _uploadedKeys;
                }
            }
        }
    }
}
