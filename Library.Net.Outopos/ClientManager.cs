﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Library.Net.Connections;
using Library.Net.Proxy;

namespace Library.Net.Outopos
{
    public delegate Cap CreateCapEventHandler(object sender, string uri);

    class ClientManager : ManagerBase, Library.Configuration.ISettings
    {
        private BufferManager _bufferManager;

        private Settings _settings;

        private Regex _regex = new Regex(@"(.*?):(.*):(\d*)", RegexOptions.Compiled);
        private Regex _regex2 = new Regex(@"(.*?):(.*)", RegexOptions.Compiled);

        private CreateCapEventHandler _createCapEvent;
        private CheckUriEventHandler _checkUriEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxReceiveCount = 1024 * 1024 * 8;

        public ClientManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings(_thisLock);
        }

        public CreateCapEventHandler CreateCapEvent
        {
            set
            {
                lock (_thisLock)
                {
                    _createCapEvent = value;
                }
            }
        }

        public CheckUriEventHandler CheckUriEvent
        {
            set
            {
                lock (_thisLock)
                {
                    _checkUriEvent = value;
                }
            }
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.ConnectionFilters;
                }
            }
        }

        protected virtual Cap OnCreateCapEvent(string uri)
        {
            return _createCapEvent?.Invoke(this, uri);
        }

        protected virtual bool OnCheckUriEvent(string uri)
        {
            return _checkUriEvent?.Invoke(this, uri) ?? true;
        }

        private static IEnumerable<KeyValuePair<string, string>> Decode(string option)
        {
            try
            {
                var pair = new Dictionary<string, string>();
                var kl = new List<char>();
                var vl = new List<char>();
                bool keyFlag = true;
                bool wordFlag = false;

                for (int i = 0; i < option.Length; i++)
                {
                    char w1;
                    char? w2 = null;

                    w1 = option[i];
                    if (option.Length > i + 1) w2 = option[i + 1];

                    if (keyFlag)
                    {
                        if (w1 == '=')
                        {
                            keyFlag = false;
                        }
                        else
                        {
                            kl.Add(w1);
                        }
                    }
                    else
                    {
                        if (w1 == '\\' && w2.HasValue)
                        {
                            if (w2.Value == '\"' || w2.Value == '\\')
                            {
                                vl.Add(w2.Value);
                                i++;
                            }
                            else
                            {
                                vl.Add(w1);
                            }
                        }
                        else
                        {
                            if (wordFlag)
                            {
                                if (w1 == '\"')
                                {
                                    wordFlag = false;
                                }
                                else
                                {
                                    vl.Add(w1);
                                }
                            }
                            else
                            {
                                if (w1 == '\"')
                                {
                                    wordFlag = true;
                                }
                                else if (w1 == ' ')
                                {
                                    var key = new string(kl.ToArray());
                                    var value = new string(vl.ToArray());

                                    if (!string.IsNullOrWhiteSpace(key))
                                    {
                                        pair[key.Trim()] = value;
                                    }

                                    kl.Clear();
                                    vl.Clear();

                                    keyFlag = true;
                                }
                                else
                                {
                                    vl.Add(w1);
                                }
                            }
                        }
                    }
                }

                if (!keyFlag)
                {
                    var key = new string(kl.ToArray());
                    var value = new string(vl.ToArray());

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        pair[key.Trim()] = value;
                    }
                }

                return pair;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IPAddress GetIpAddress(string host)
        {
            IPAddress remoteIP = null;

            if (!IPAddress.TryParse(host, out remoteIP))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(host);

                if (hostEntry.AddressList.Length > 0)
                {
                    remoteIP = hostEntry.AddressList[0];
                }
                else
                {
                    return null;
                }
            }

            return remoteIP;
        }

        private static Socket Connect(IPEndPoint remoteEndPoint, TimeSpan timeout)
        {
            Socket socket = null;

            try
            {
                socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.ReceiveTimeout = (int)timeout.TotalMilliseconds;
                socket.SendTimeout = (int)timeout.TotalMilliseconds;

                var asyncResult = socket.BeginConnect(remoteEndPoint, null, null);

                if (!asyncResult.IsCompleted && !asyncResult.CompletedSynchronously)
                {
                    if (!asyncResult.AsyncWaitHandle.WaitOne(timeout, false))
                    {
                        throw new ConnectionException();
                    }
                }

                socket.EndConnect(asyncResult);

                return socket;
            }
            catch (Exception)
            {
                if (socket != null) socket.Dispose();
            }

            throw new ClientManagerException();
        }

        public Connection CreateConnection(string uri, BandwidthLimit bandwidthLimit)
        {
            var garbages = new List<IDisposable>();

            try
            {
                Connection connection = null;

                if (connection == null)
                {
                    // Overlay network
                    var cap = this.OnCreateCapEvent(uri);
                    if (cap == null) goto End;

                    garbages.Add(cap);

                    connection = new BaseConnection(cap, bandwidthLimit, _maxReceiveCount, _bufferManager);
                    garbages.Add(connection);

                    End:;
                }

                if (connection == null)
                {
                    ConnectionFilter connectionFilter = null;

                    lock (_thisLock)
                    {
                        foreach (var filter in this.Filters)
                        {
                            if (filter.UriCondition.IsMatch(uri))
                            {
                                if (filter.ConnectionType != ConnectionType.None)
                                {
                                    connectionFilter = filter;
                                }

                                break;
                            }
                        }
                    }

                    if (connectionFilter == null) return null;

                    string scheme = null;
                    string host = null;
                    int port = -1;

                    {
                        var match = _regex.Match(uri);

                        if (match.Success)
                        {
                            scheme = match.Groups[1].Value;
                            host = match.Groups[2].Value;
                            port = int.Parse(match.Groups[3].Value);
                        }
                        else
                        {
                            var match2 = _regex2.Match(uri);

                            if (match2.Success)
                            {
                                scheme = match2.Groups[1].Value;
                                host = match2.Groups[2].Value;
                                port = 4050;
                            }
                        }
                    }

                    if (host == null) return null;

                    IList<KeyValuePair<string, string>> options = null;

                    if (!string.IsNullOrWhiteSpace(connectionFilter.Option))
                    {
                        options = ClientManager.Decode(connectionFilter.Option).OfType<KeyValuePair<string, string>>().ToList();
                    }

                    if (connectionFilter.ConnectionType == ConnectionType.Tcp)
                    {
                        var ipAddress = ClientManager.GetIpAddress(host);
                        if (ipAddress == null) return null;

                        host = ipAddress.ToString();
                        uri = string.Format("{0}:{1}:{2}", scheme, host, port);

                        if (!this.OnCheckUriEvent(uri))
                        {
                            return null;
                        }

#if !DEBUG
                        // Check
                        {
                            Uri url = new Uri(string.Format("{0}://{1}:{2}", scheme, host, port));

                            if (url.HostNameType == UriHostNameType.IPv4)
                            {
                                if (IPAddress.Any.ToString() == ipAddress.ToString()
                                    || IPAddress.Loopback.ToString() == ipAddress.ToString()
                                    || IPAddress.Broadcast.ToString() == ipAddress.ToString())
                                {
                                    return null;
                                }
                                if (CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("10.0.0.0").GetAddressBytes()) >= 0
                                    && CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("10.255.255.255").GetAddressBytes()) <= 0)
                                {
                                    return null;
                                }
                                if (CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("172.16.0.0").GetAddressBytes()) >= 0
                                    && CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("172.31.255.255").GetAddressBytes()) <= 0)
                                {
                                    return null;
                                }
                                if (CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("127.0.0.0").GetAddressBytes()) >= 0
                                    && CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("127.255.255.255").GetAddressBytes()) <= 0)
                                {
                                    return null;
                                }
                                if (CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("192.168.0.0").GetAddressBytes()) >= 0
                                    && CollectionUtilities.Compare(ipAddress.GetAddressBytes(), IPAddress.Parse("192.168.255.255").GetAddressBytes()) <= 0)
                                {
                                    return null;
                                }
                            }
                            else if (url.HostNameType == UriHostNameType.IPv6)
                            {
                                if (IPAddress.IPv6Any.ToString() == ipAddress.ToString()
                                    || IPAddress.IPv6Loopback.ToString() == ipAddress.ToString()
                                    || IPAddress.IPv6None.ToString() == ipAddress.ToString())
                                {
                                    return null;
                                }
                                if (ipAddress.ToString().ToLower().StartsWith("fe80:"))
                                {
                                    return null;
                                }
                            }
                        }
#endif

                        var socket = ClientManager.Connect(new IPEndPoint(ipAddress, port), new TimeSpan(0, 0, 10));
                        garbages.Add(socket);

                        var cap = new SocketCap(socket);
                        garbages.Add(cap);

                        connection = new BaseConnection(cap, bandwidthLimit, _maxReceiveCount, _bufferManager);
                        garbages.Add(connection);
                    }
                    else
                    {
                        if (!this.OnCheckUriEvent(uri))
                        {
                            return null;
                        }

                        string proxyScheme = null;
                        string proxyHost = null;
                        int proxyPort = -1;

                        {
                            var match = _regex.Match(connectionFilter.ProxyUri);

                            if (match.Success)
                            {
                                proxyScheme = match.Groups[1].Value;
                                proxyHost = match.Groups[2].Value;
                                proxyPort = int.Parse(match.Groups[3].Value);
                            }
                            else
                            {
                                var match2 = _regex2.Match(connectionFilter.ProxyUri);

                                if (match2.Success)
                                {
                                    proxyScheme = match2.Groups[1].Value;
                                    proxyHost = match2.Groups[2].Value;

                                    if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy
                                        || connectionFilter.ConnectionType == ConnectionType.Socks4aProxy
                                        || connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                                    {
                                        proxyPort = 1080;
                                    }
                                    else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                                    {
                                        proxyPort = 80;
                                    }
                                }
                            }
                        }

                        if (proxyHost == null) return null;

                        if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy
                            || connectionFilter.ConnectionType == ConnectionType.Socks4aProxy
                            || connectionFilter.ConnectionType == ConnectionType.Socks5Proxy
                            || connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                        {
                            var socket = ClientManager.Connect(new IPEndPoint(ClientManager.GetIpAddress(proxyHost), proxyPort), new TimeSpan(0, 0, 10));
                            garbages.Add(socket);

                            ProxyClientBase proxy = null;

                            if (connectionFilter.ConnectionType == ConnectionType.Socks4Proxy)
                            {
                                var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;
                                proxy = new Socks4ProxyClient(socket, user, host, port);
                            }
                            else if (connectionFilter.ConnectionType == ConnectionType.Socks4aProxy)
                            {
                                var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;
                                proxy = new Socks4aProxyClient(socket, user, host, port);
                            }
                            else if (connectionFilter.ConnectionType == ConnectionType.Socks5Proxy)
                            {
                                var user = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("user")).Select(n => n.Value).FirstOrDefault() : null;
                                var pass = (options != null) ? options.Where(n => n.Key.ToLower().StartsWith("pass")).Select(n => n.Value).FirstOrDefault() : null;
                                proxy = new Socks5ProxyClient(socket, user, pass, host, port);
                            }
                            else if (connectionFilter.ConnectionType == ConnectionType.HttpProxy)
                            {
                                proxy = new HttpProxyClient(socket, host, port);
                            }

                            var cap = new SocketCap(proxy.Create(new TimeSpan(0, 0, 30)));
                            garbages.Add(cap);

                            connection = new BaseConnection(cap, bandwidthLimit, _maxReceiveCount, _bufferManager);
                            garbages.Add(connection);
                        }
                    }
                }

                if (connection == null) return null;

                var secureConnection = new SecureConnection(SecureConnectionVersion.Version3, SecureConnectionType.Connect, connection, null, _bufferManager);
                garbages.Add(secureConnection);

                secureConnection.Connect(new TimeSpan(0, 0, 30));

                var compressConnection = new CompressConnection(secureConnection, _maxReceiveCount, _bufferManager);
                garbages.Add(compressConnection);

                compressConnection.Connect(new TimeSpan(0, 0, 10));

                return compressConnection;
            }
            catch (Exception)
            {
                foreach (var item in garbages)
                {
                    item.Dispose();
                }
            }

            return null;
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);
            }
        }

        public void Save(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<ConnectionFilterCollection>() { Name = "ConnectionFilters", Value = new ConnectionFilterCollection() },
                 })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public ConnectionFilterCollection ConnectionFilters
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (ConnectionFilterCollection)this["ConnectionFilters"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }
        }
    }

    [Serializable]
    class ClientManagerException : ManagerException
    {
        public ClientManagerException() : base() { }
        public ClientManagerException(string message) : base(message) { }
        public ClientManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
