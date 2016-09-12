using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "UploadState")]
    public enum UploadState
    {
        [EnumMember(Value = "ComputeHash")]
        ComputeHash = 0,

        [EnumMember(Value = "Encoding")]
        Encoding = 1,

        [EnumMember(Value = "ParityEncoding")]
        ParityEncoding = 2,

        [EnumMember(Value = "Uploading")]
        Uploading = 3,

        [EnumMember(Value = "Completed")]
        Completed = 4,

        [EnumMember(Value = "Error")]
        Error = 5,
    }

    [DataContract(Name = "UploadType")]
    enum UploadType
    {
        [EnumMember(Value = "Upload")]
        Upload = 0,

        [EnumMember(Value = "Share")]
        Share = 1,
    }

    [DataContract(Name = "UploadItem")]
    sealed class UploadItem
    {
        private UploadType _type;
        private UploadState _state;
        private int _priority = 3;

        private string _filePath;

        private string _name;
        private long _length;
        private DateTime _creationTime;
        private KeywordCollection _keywords;
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

        private long _encodeOffset;
        private long _encodeLength;

        private List<Key> _LockedKeys;
        private HashSet<Key> _uploadKeys;
        private HashSet<Key> _uploadedKeys;
        private HashSet<Key> _retainKeys;

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
        public UploadType Type
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
        public UploadState State
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

        [DataMember(Name = "FilePath")]
        public string FilePath
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _filePath;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _filePath = value;
                }
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

        [DataMember(Name = "Keywords")]
        public KeywordCollection Keywords
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keywords == null)
                        _keywords = new KeywordCollection();

                    return _keywords;
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

        [DataMember(Name = "EncodeOffset")]
        public long EncodeOffset
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _encodeOffset;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _encodeOffset = value;
                }
            }
        }

        [DataMember(Name = "EncodeLength")]
        public long EncodeLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _encodeLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _encodeLength = value;
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

        [DataMember(Name = "RetainKeys")]
        public HashSet<Key> RetainKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_retainKeys == null)
                        _retainKeys = new HashSet<Key>();

                    return _retainKeys;
                }
            }
        }
    }
}
