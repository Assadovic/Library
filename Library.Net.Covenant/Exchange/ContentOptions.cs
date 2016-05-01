using System.Runtime.Serialization;

namespace Library.Net.Covenant
{
    [DataContract(Name = "ContentOptions", Namespace = "http://Library/Net/Covenant")]
    sealed class ContentOptions
    {
        private Bitmap _bitmap;
        private Index _index;
        private string _path;

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

        [DataMember(Name = "Bitmap")]
        public Bitmap Bitmap
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _bitmap;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _bitmap = value;
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
    }
}
