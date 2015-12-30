using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ConnectionType", Namespace = "http://Library/Net/Outopos")]
    public enum ConnectionType
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Tcp")]
        Tcp = 1,

        [EnumMember(Value = "Socks4Proxy")]
        Socks4Proxy = 2,

        [EnumMember(Value = "Socks4aProxy")]
        Socks4aProxy = 3,

        [EnumMember(Value = "Socks5Proxy")]
        Socks5Proxy = 4,

        [EnumMember(Value = "HttpProxy")]
        HttpProxy = 5,
    }

    [DataContract(Name = "ConnectionFilter", Namespace = "http://Library/Net/Outopos")]
    public sealed class ConnectionFilter : IEquatable<ConnectionFilter>, IThisLock
    {
        private ConnectionType _connectionType;
        private string _proxyUri;
        private UriCondition _uriCondition;
        private string _option;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        public ConnectionFilter(ConnectionType connectionType, string proxyUri, UriCondition uriCondition, string option)
        {
            this.ConnectionType = connectionType;
            this.ProxyUri = proxyUri;
            this.UriCondition = uriCondition;
            this.Option = option;
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return (int)this.ConnectionType;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ConnectionFilter)) return false;

            return this.Equals((ConnectionFilter)obj);
        }

        public bool Equals(ConnectionFilter other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.ConnectionType != other.ConnectionType)
                || (this.ProxyUri != other.ProxyUri)
                || (this.UriCondition != other.UriCondition)
                || (this.Option != other.Option))
            {
                return false;
            }

            return true;
        }

        [DataMember(Name = "ConnectionType")]
        public ConnectionType ConnectionType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _connectionType;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _connectionType = value;
                }
            }
        }

        [DataMember(Name = "ProxyUri")]
        public string ProxyUri
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _proxyUri;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _proxyUri = value;
                }
            }
        }

        [DataMember(Name = "UriCondition")]
        public UriCondition UriCondition
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _uriCondition;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _uriCondition = value;
                }
            }
        }

        [DataMember(Name = "Option")]
        public string Option
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _option;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _option = value;
                }
            }
        }

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

    [DataContract(Name = "UriCondition", Namespace = "http://Library/Net/Outopos")]
    public sealed class UriCondition : IEquatable<UriCondition>, IThisLock
    {
        private string _value;
        private Regex _regex;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        public UriCondition(string value)
        {
            this.Value = value;
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return this.Value.Length;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is UriCondition)) return false;

            return this.Equals((UriCondition)obj);
        }

        public bool Equals(UriCondition other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Value != other.Value)
            {
                return false;
            }

            return true;
        }

        public bool IsMatch(string uri)
        {
            lock (this.ThisLock)
            {
                if (_regex == null) return false;
                else return _regex.IsMatch(uri);
            }
        }

        [DataMember(Name = "Value")]
        public string Value
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _value;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _value = value;

                    if (value == null) _regex = null;
                    else _regex = new Regex(value, RegexOptions.Compiled);
                }
            }
        }

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
