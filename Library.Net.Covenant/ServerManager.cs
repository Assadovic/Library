using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Library.Io;
using Library.Net.Connections;

namespace Library.Net.Covenant
{
    public delegate Cap AcceptCapEventHandler(object sender, out string uri);

    class ServerManager : StateManagerBase, IThisLock
    {
        private BufferManager _bufferManager;
        private BandwidthLimit _bandwidthLimit;

        private List<Thread> _watchThreads = new List<Thread>();

        private ConcurrentQueue<AcceptResult> _searchAcceptResults = new ConcurrentQueue<AcceptResult>();
        private ConcurrentQueue<AcceptResult> _exchangeAcceptResults = new ConcurrentQueue<AcceptResult>();

        private volatile ManagerState _state = ManagerState.Stop;

        private AcceptCapEventHandler _acceptCapEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxReceiveCount = 1024 * 1024 * 8;

        public ServerManager(BufferManager bufferManager, BandwidthLimit bandwidthLimit)
        {
            _bufferManager = bufferManager;
            _bandwidthLimit = bandwidthLimit;
        }

        public BandwidthLimit BandwidthLimit
        {
            get
            {
                return _bandwidthLimit;
            }
        }

        public AcceptCapEventHandler AcceptCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _acceptCapEvent = value;
                }
            }
        }

        protected virtual Cap OnAcceptCapEvent(out string uri)
        {
            uri = null;
            return _acceptCapEvent?.Invoke(this, out uri);
        }

        public Connection AcceptConnection(out string uri, out ProtocolVersion version, ProtocolType type)
        {
            uri = null;
            version = 0;

            AcceptResult result = null;

            if (type == ProtocolType.Search)
            {
                _searchAcceptResults.TryDequeue(out result);
            }
            else if (type == ProtocolType.Exchange)
            {
                _exchangeAcceptResults.TryDequeue(out result);
            }

            if (result == null) throw new ServerManagerException();

            uri = result.Uri;
            version = result.Version;

            return result.Connection;
        }

        private void WatchThread()
        {
            for (;;)
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var garbages = new List<IDisposable>();

                try
                {
                    Connection connection = null;
                    string uri;
                    {
                        var cap = this.OnAcceptCapEvent(out uri);
                        if (cap == null) goto End;

                        garbages.Add(cap);

                        connection = new BaseConnection(cap, _bandwidthLimit, _maxReceiveCount, _bufferManager);
                        garbages.Add(connection);

                        End:;
                    }

                    if (connection == null) continue;

                    ProtocolVersion version;
                    ProtocolType type;

                    version = this.Handshake(connection, out type);

                    if (version == ProtocolVersion.Version1)
                    {
                        if (type == ProtocolType.Search)
                        {
                            if (_searchAcceptResults.Count > 3) throw new ConnectionException();

                            var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
                            garbages.Add(compressConnection);

                            compressConnection.Connect(new TimeSpan(0, 0, 10));

                            _searchAcceptResults.Enqueue(new AcceptResult(compressConnection, uri, version));
                        }
                        else if (type == ProtocolType.Exchange)
                        {
                            if (_exchangeAcceptResults.Count > 3) throw new ConnectionException();

                            var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
                            garbages.Add(compressConnection);

                            compressConnection.Connect(new TimeSpan(0, 0, 10));

                            _exchangeAcceptResults.Enqueue(new AcceptResult(compressConnection, uri, version));
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
                catch (Exception)
                {
                    foreach (var item in garbages)
                    {
                        item.Dispose();
                    }
                }
            }
        }

        private class AcceptResult
        {
            public AcceptResult(Connection connection, string uri, ProtocolVersion version)
            {
                this.Connection = connection;
                this.Uri = uri;
                this.Version = version;
            }

            public Connection Connection { get; private set; }
            public string Uri { get; private set; }
            public ProtocolVersion Version { get; private set; }
        }

        private ProtocolVersion Handshake(Connection connection, out ProtocolType protocolType)
        {
            protocolType = 0;

            var timeout = new TimeSpan(0, 0, 30);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            ProtocolVersion protocolVersion;

            {
                var myProtocolVersion = ProtocolVersion.Version1;

                using (BufferStream stream = new BufferStream(_bufferManager))
                using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                {
                    xml.WriteStartDocument();

                    xml.WriteStartElement("Protocol");

                    if (myProtocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        xml.WriteStartElement("Covenant");
                        xml.WriteAttributeString("Version", "1");
                        xml.WriteEndElement(); //Covenant
                    }

                    xml.WriteEndElement(); //Protocol

                    xml.WriteEndDocument();
                    xml.Flush();
                    stream.Flush();

                    stream.Seek(0, SeekOrigin.Begin);
                    connection.Send(stream, timeout - stopwatch.Elapsed);
                }

                var otherProtocolVersion = (ProtocolVersion)0;

                using (Stream stream = connection.Receive(timeout - stopwatch.Elapsed))
                using (XmlTextReader xml = new XmlTextReader(stream))
                {
                    while (xml.Read())
                    {
                        if (xml.NodeType == XmlNodeType.Element)
                        {
                            if (xml.LocalName == "Covenant")
                            {
                                var version = xml.GetAttribute("Version");

                                if (version == "1")
                                {
                                    otherProtocolVersion |= ProtocolVersion.Version1;
                                }
                            }
                        }
                    }
                }

                protocolVersion = myProtocolVersion & otherProtocolVersion;
            }

            if (protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                using (Stream stream = connection.Receive(timeout - stopwatch.Elapsed))
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    var line = reader.ReadLine();
                    if (line == null) throw new ConnectionException();

                    if (line == "Search")
                    {
                        protocolType = ProtocolType.Search;
                    }
                    else if (line == "Exchange")
                    {
                        protocolType = ProtocolType.Exchange;
                    }
                }
            }

            return protocolVersion;
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    for (int i = 0; i < 3; i++)
                    {
                        var thread = new Thread(this.WatchThread);
                        thread.Name = "ServerManager_WatchThread";
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Start();

                        _watchThreads.Add(thread);
                    }
                }
            }
        }

        public override void Stop()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;

                    foreach (var thread in _watchThreads)
                    {
                        thread.Join();
                    }
                    _watchThreads.Clear();
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

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }

    [Serializable]
    class ServerManagerException : StateManagerException
    {
        public ServerManagerException() : base() { }
        public ServerManagerException(string message) : base(message) { }
        public ServerManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
