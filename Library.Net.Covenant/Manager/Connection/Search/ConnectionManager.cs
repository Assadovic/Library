using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Library.Collections;
using Library.Io;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Covenant.Search
{
    [Flags]
    [DataContract(Name = "ProtocolVersion", Namespace = "http://Library/Net/Covenant/Search")]
    enum ProtocolVersion
    {
        [EnumMember(Value = "Version1")]
        Version1 = 0x01,
    }

    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullLocationsRequestEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullLocationsEventArgs : EventArgs
    {
        public IEnumerable<Location> Locations { get; set; }
    }

    class PullMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<QueryMetadata> QueryMetadatas { get; set; }
    }

    class PullMetadatasEventArgs : EventArgs
    {
        public IEnumerable<Metadata> Metadatas { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullLocationsRequestEventHandler(object sender, PullLocationsRequestEventArgs e);
    delegate void PullLocationsEventHandler(object sender, PullLocationsEventArgs e);

    delegate void PullMetadatasRequestEventHandler(object sender, PullMetadatasRequestEventArgs e);
    delegate void PullMetadatasEventHandler(object sender, PullMetadatasEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    [DataContract(Name = "ConnectDirection", Namespace = "http://Library/Net/Covenant/Search")]
    public enum ConnectDirection
    {
        [EnumMember(Value = "In")]
        In = 0,

        [EnumMember(Value = "Out")]
        Out = 1,
    }

    class ConnectionManager : ManagerBase, IThisLock
    {
        private enum SerializeId : byte
        {
            Alive = 0,
            Cancel = 1,

            Ping = 2,
            Pong = 3,

            Nodes = 4,

            LocationsRequest = 5,
            Locations = 6,

            MetadatasRequest = 7,
            Metadatas = 8,
        }

        private ProtocolVersion _protocolVersion;
        private ProtocolVersion _myProtocolVersion;
        private ProtocolVersion _otherProtocolVersion;
        private Connection _connection;
        private byte[] _mySessionId;
        private byte[] _otherSessionId;
        private Node _baseNode;
        private Node _otherNode;
        private ConnectDirection _direction;
        private BufferManager _bufferManager;

        private bool _onClose;

        private byte[] _pingHash;
        private Stopwatch _responseStopwatch = new Stopwatch();

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 3, 0);

        private WatchTimer _aliveTimer;
        private Stopwatch _aliveStopwatch = new Stopwatch();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 1024;
        private const int _maxLocationRequestCount = 1024;
        private const int _maxLocationCount = 1024;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 1024;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullLocationsRequestEventHandler PullLocationsRequestEvent;
        public event PullLocationsEventHandler PullLocationsEvent;

        public event PullMetadatasRequestEventHandler PullMetadatasRequestEvent;
        public event PullMetadatasEventHandler PullMetadatasEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(Connection connection, byte[] mySessionId, Node baseNode, ConnectDirection direction, BufferManager bufferManager)
        {
            _myProtocolVersion = ProtocolVersion.Version1;
            _connection = connection;
            _mySessionId = mySessionId;
            _baseNode = baseNode;
            _direction = direction;
            _bufferManager = bufferManager;
        }

        public byte[] SesstionId
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _otherSessionId;
                }
            }
        }

        public Node Node
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _otherNode;
                }
            }
        }

        public ConnectDirection Direction
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _direction;
                }
            }
        }

        public Connection Connection
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _connection;
                }
            }
        }

        public ProtocolVersion ProtocolVersion
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _protocolVersion;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public TimeSpan ResponseTime
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _responseStopwatch.Elapsed;
            }
        }

        public void Connect()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    var timeout = new TimeSpan(0, 0, 30);

                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                    {
                        xml.WriteStartDocument();

                        xml.WriteStartElement("Protocol");

                        if (_myProtocolVersion.HasFlag(ProtocolVersion.Version1))
                        {
                            xml.WriteStartElement("Covenant_Search");
                            xml.WriteAttributeString("Version", "1");
                            xml.WriteEndElement(); //Covenant
                        }

                        xml.WriteEndElement(); //Protocol

                        xml.WriteEndDocument();
                        xml.Flush();
                        stream.Flush();

                        stream.Seek(0, SeekOrigin.Begin);
                        _connection.Send(stream, timeout - stopwatch.Elapsed);
                    }

                    using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                    using (XmlTextReader xml = new XmlTextReader(stream))
                    {
                        while (xml.Read())
                        {
                            if (xml.NodeType == XmlNodeType.Element)
                            {
                                if (xml.LocalName == "Covenant_Search")
                                {
                                    var version = xml.GetAttribute("Version");

                                    if (version == "1")
                                    {
                                        _otherProtocolVersion |= ProtocolVersion.Version1;
                                    }
                                }
                            }
                        }
                    }

                    _protocolVersion = _myProtocolVersion & _otherProtocolVersion;

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = new MemoryStream(_mySessionId))
                        {
                            _connection.Send(stream, timeout - stopwatch.Elapsed);
                        }

                        using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                        {
                            if (stream.Length > 32) throw new SearchConnectionManagerException();

                            _otherSessionId = new byte[stream.Length];
                            stream.Read(_otherSessionId, 0, _otherSessionId.Length);
                        }

                        using (Stream stream = _baseNode.Export(_bufferManager))
                        {
                            _connection.Send(stream, timeout - stopwatch.Elapsed);
                        }

                        using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                        {
                            _otherNode = Node.Import(stream, _bufferManager);
                        }

                        _aliveStopwatch.Restart();

                        _pingHash = new byte[32];

                        using (var rng = RandomNumberGenerator.Create())
                        {
                            rng.GetBytes(_pingHash);
                        }

                        _responseStopwatch.Start();
                        this.Ping(_pingHash);

                        Task.Factory.StartNew(this.PullThread, TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent);
                        _aliveTimer = new WatchTimer(this.AliveTimer, new TimeSpan(0, 0, 30));
                    }
                    else
                    {
                        throw new SearchConnectionManagerException();
                    }
                }
                catch (Exception ex)
                {
                    throw new SearchConnectionManagerException(ex.Message, ex);
                }
            }
        }

        public void Close()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    _connection.Close(new TimeSpan(0, 0, 30));

                    this.OnClose(new EventArgs());
                }
                catch (Exception ex)
                {
                    throw new SearchConnectionManagerException(ex.Message, ex);
                }
            }
        }

        private void AliveTimer()
        {
            if (_disposed) return;

            Thread.CurrentThread.Name = "ConnectionManager_AliveTimer";

            try
            {
                if (_aliveStopwatch.Elapsed > _aliveTimeSpan)
                {
                    this.Alive();
                }
            }
            catch (Exception)
            {
                this.OnClose(new EventArgs());
            }
        }

        private void Alive()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Alive);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    if (!_disposed)
                    {
                        this.OnClose(new EventArgs());
                    }

                    throw;
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        private void Ping(byte[] value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Ping);
                        stream.Write(value, 0, value.Length);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        private void Pong(byte[] value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Pong);
                        stream.Write(value, 0, value.Length);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        private void PullThread()
        {
            Thread.CurrentThread.Name = "ConnectionManager_PullThread";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            try
            {
                var sw = new Stopwatch();

                for (;;)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    sw.Restart();

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = _connection.Receive(_receiveTimeSpan))
                        {
                            if (stream.Length == 0) continue;

                            var type = (byte)stream.ReadByte();

                            using (Stream stream2 = new RangeStream(stream, 1, stream.Length - 1, true))
                            {
                                try
                                {
                                    if (type == (byte)SerializeId.Alive)
                                    {

                                    }
                                    else if (type == (byte)SerializeId.Cancel)
                                    {
                                        this.OnPullCancel(new EventArgs());
                                    }
                                    else if (type == (byte)SerializeId.Ping)
                                    {
                                        if (stream2.Length > 32) continue;

                                        var buffer = new byte[stream2.Length];
                                        stream2.Read(buffer, 0, buffer.Length);

                                        this.Pong(buffer);
                                    }
                                    else if (type == (byte)SerializeId.Pong)
                                    {
                                        if (stream2.Length > 32) continue;

                                        var buffer = new byte[stream2.Length];
                                        stream2.Read(buffer, 0, buffer.Length);

                                        if (!CollectionUtilities.Equals(buffer, _pingHash)) continue;

                                        _responseStopwatch.Stop();
                                    }
                                    else if (type == (byte)SerializeId.Nodes)
                                    {
                                        var message = NodesMessage.Import(stream2, _bufferManager);
                                        this.OnPullNodes(new PullNodesEventArgs() { Nodes = message.Nodes });
                                    }
                                    else if (type == (byte)SerializeId.LocationsRequest)
                                    {
                                        var message = LocationsRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullLocationsRequest(new PullLocationsRequestEventArgs()
                                        {
                                            Keys = message.Keys,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.Locations)
                                    {
                                        var message = LocationsMessage.Import(stream2, _bufferManager);

                                        this.OnPullLocations(new PullLocationsEventArgs()
                                        {
                                            Locations = message.Locations,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MetadatasRequest)
                                    {
                                        var message = MetadatasRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullMetadatasRequest(new PullMetadatasRequestEventArgs()
                                        {
                                            QueryMetadatas = message.QueryMetadatas,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.Metadatas)
                                    {
                                        var message = MetadatasMessage.Import(stream2, _bufferManager);

                                        this.OnPullMetadatas(new PullMetadatasEventArgs()
                                        {
                                            Metadatas = message.Metadatas,
                                        });
                                    }
                                }
                                catch (Exception)
                                {

                                }
                            }
                        }
                    }
                    else
                    {
                        throw new SearchConnectionManagerException();
                    }

                    sw.Stop();

                    if (300 > sw.ElapsedMilliseconds) Thread.Sleep(300 - (int)sw.ElapsedMilliseconds);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);

                if (!_disposed)
                {
                    this.OnClose(new EventArgs());
                }
            }
        }

        protected virtual void OnPullNodes(PullNodesEventArgs e)
        {
            if (this.PullNodesEvent != null)
            {
                this.PullNodesEvent(this, e);
            }
        }

        protected virtual void OnPullLocationsRequest(PullLocationsRequestEventArgs e)
        {
            if (this.PullLocationsRequestEvent != null)
            {
                this.PullLocationsRequestEvent(this, e);
            }
        }

        protected virtual void OnPullLocations(PullLocationsEventArgs e)
        {
            if (this.PullLocationsEvent != null)
            {
                this.PullLocationsEvent(this, e);
            }
        }

        protected virtual void OnPullMetadatasRequest(PullMetadatasRequestEventArgs e)
        {
            if (this.PullMetadatasRequestEvent != null)
            {
                this.PullMetadatasRequestEvent(this, e);
            }
        }

        protected virtual void OnPullMetadatas(PullMetadatasEventArgs e)
        {
            if (this.PullMetadatasEvent != null)
            {
                this.PullMetadatasEvent(this, e);
            }
        }

        protected virtual void OnPullCancel(EventArgs e)
        {
            if (this.PullCancelEvent != null)
            {
                this.PullCancelEvent(this, e);
            }
        }

        protected virtual void OnClose(EventArgs e)
        {
            if (_onClose) return;
            _onClose = true;

            if (this.CloseEvent != null)
            {
                this.CloseEvent(this, e);
            }
        }

        public void PushNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Nodes);
                    stream.Flush();

                    var message = new NodesMessage(nodes);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        public void PushLocationsRequest(IEnumerable<Key> keys)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.LocationsRequest);
                    stream.Flush();

                    var message = new LocationsRequestMessage(keys);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        public void PushLocations(IEnumerable<Location> multicastMetadatas)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Locations);
                    stream.Flush();

                    var message = new LocationsMessage(multicastMetadatas);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        public void PushMetadatasRequest(IEnumerable<QueryMetadata> queryMetadatas)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MetadatasRequest);
                    stream.Flush();

                    var message = new MetadatasRequestMessage(queryMetadatas);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        public void PushMetadatas(IEnumerable<Metadata> metadats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Metadatas);
                    stream.Flush();

                    var message = new MetadatasMessage(metadats);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    _connection.Send(stream, _sendTimeSpan);
                    _aliveStopwatch.Restart();
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
                finally
                {
                    stream.Close();
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        public void PushCancel()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                try
                {
                    using (Stream stream = new BufferStream(_bufferManager))
                    {
                        stream.WriteByte((byte)SerializeId.Cancel);
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

                        _connection.Send(stream, _sendTimeSpan);
                        _aliveStopwatch.Restart();
                    }
                }
                catch (ConnectionException)
                {
                    this.OnClose(new EventArgs());

                    throw;
                }
            }
            else
            {
                throw new SearchConnectionManagerException();
            }
        }

        #region Message

        private sealed class NodesMessage : ItemBase<NodesMessage>
        {
            private enum SerializeId : byte
            {
                Node = 0,
            }

            private volatile NodeCollection _nodes;

            public NodesMessage(IEnumerable<Node> nodes)
            {
                if (nodes != null) this.ProtectedNodes.AddRange(nodes);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                for (;;)
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Node)
                        {
                            this.ProtectedNodes.Add(Node.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Nodes
                foreach (var value in this.Nodes)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Node, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<Node> _readOnlyNodes;

            public IEnumerable<Node> Nodes
            {
                get
                {
                    if (_readOnlyNodes == null)
                        _readOnlyNodes = new ReadOnlyCollection<Node>(this.ProtectedNodes.ToArray());

                    return _readOnlyNodes;
                }
            }

            private NodeCollection ProtectedNodes
            {
                get
                {
                    if (_nodes == null)
                        _nodes = new NodeCollection(_maxNodeCount);

                    return _nodes;
                }
            }
        }

        private sealed class LocationsRequestMessage : ItemBase<LocationsRequestMessage>
        {
            private enum SerializeId : byte
            {
                Key = 0,
            }

            private KeyCollection _keys;

            public LocationsRequestMessage(IEnumerable<Key> keys)
            {
                if (keys != null) this.ProtectedKeys.AddRange(keys);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                for (;;)
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Key)
                        {
                            this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Keys
                foreach (var value in this.Keys)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<Key> _readOnlyKeys;

            public IEnumerable<Key> Keys
            {
                get
                {
                    if (_readOnlyKeys == null)
                        _readOnlyKeys = new ReadOnlyCollection<Key>(this.ProtectedKeys.ToArray());

                    return _readOnlyKeys;
                }
            }

            [DataMember(Name = "Keys")]
            private KeyCollection ProtectedKeys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection(_maxLocationRequestCount);

                    return _keys;
                }
            }
        }

        private sealed class LocationsMessage : ItemBase<LocationsMessage>
        {
            private enum SerializeId : byte
            {
                Location = 0,
            }

            private LockedList<Location> _locations;

            public LocationsMessage(IEnumerable<Location> locations)
            {
                if (locations != null) this.ProtectedLocations.AddRange(locations);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                for (;;)
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Location)
                        {
                            this.ProtectedLocations.Add(Location.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Locations
                foreach (var value in this.Locations)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Location, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<Location> _readOnlyLocations;

            public IEnumerable<Location> Locations
            {
                get
                {
                    if (_readOnlyLocations == null)
                        _readOnlyLocations = new ReadOnlyCollection<Location>(this.ProtectedLocations.ToArray());

                    return _readOnlyLocations;
                }
            }

            [DataMember(Name = "Locations")]
            private LockedList<Location> ProtectedLocations
            {
                get
                {
                    if (_locations == null)
                        _locations = new LockedList<Location>(_maxLocationCount);

                    return _locations;
                }
            }
        }

        private sealed class MetadatasRequestMessage : ItemBase<MetadatasRequestMessage>
        {
            private enum SerializeId : byte
            {
                QueryMetadata = 0,
            }

            private QueryMetadataCollection _queryMetadata;

            public MetadatasRequestMessage(IEnumerable<QueryMetadata> queryMetadatas)
            {
                if (queryMetadatas != null) this.ProtectedQueryMetadatas.AddRange(queryMetadatas);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                for (;;)
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.QueryMetadata)
                        {
                            this.ProtectedQueryMetadatas.Add(QueryMetadata.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Signatures
                foreach (var value in this.QueryMetadatas)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.QueryMetadata, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<QueryMetadata> _readOnlyQueryMetadatas;

            public IEnumerable<QueryMetadata> QueryMetadatas
            {
                get
                {
                    if (_readOnlyQueryMetadatas == null)
                        _readOnlyQueryMetadatas = new ReadOnlyCollection<QueryMetadata>(this.ProtectedQueryMetadatas.ToArray());

                    return _readOnlyQueryMetadatas;
                }
            }

            private QueryMetadataCollection ProtectedQueryMetadatas
            {
                get
                {
                    if (_queryMetadata == null)
                        _queryMetadata = new QueryMetadataCollection(_maxMetadataRequestCount);

                    return _queryMetadata;
                }
            }
        }

        private sealed class MetadatasMessage : ItemBase<MetadatasMessage>
        {
            private enum SerializeId : byte
            {
                Metadata = 0,
            }

            private LockedList<Metadata> _metadatas;

            public MetadatasMessage(IEnumerable<Metadata> metadatas)
            {
                if (metadatas != null) this.ProtectedMetadatas.AddRange(metadatas);
            }

            protected override void Initialize()
            {

            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                for (;;)
                {
                    byte id;
                    {
                        byte[] idBuffer = new byte[1];
                        if (stream.Read(idBuffer, 0, idBuffer.Length) != idBuffer.Length) return;
                        id = idBuffer[0];
                    }

                    int length;
                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                        length = NetworkConverter.ToInt32(lengthBuffer);
                    }

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Metadata)
                        {
                            this.ProtectedMetadatas.Add(Metadata.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                var bufferStream = new BufferStream(bufferManager);

                // Metadatas
                foreach (var value in this.Metadatas)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Metadata, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<Metadata> _readOnlyMetadatas;

            public IEnumerable<Metadata> Metadatas
            {
                get
                {
                    if (_readOnlyMetadatas == null)
                        _readOnlyMetadatas = new ReadOnlyCollection<Metadata>(this.ProtectedMetadatas.ToArray());

                    return _readOnlyMetadatas;
                }
            }

            private LockedList<Metadata> ProtectedMetadatas
            {
                get
                {
                    if (_metadatas == null)
                        _metadatas = new LockedList<Metadata>(_maxMetadataCount);

                    return _metadatas;
                }
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_aliveTimer != null)
                {
                    try
                    {
                        _aliveTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _aliveTimer = null;
                }

                if (_connection != null)
                {
                    try
                    {
                        _connection.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }
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
    class SearchConnectionManagerException : ManagerException
    {
        public SearchConnectionManagerException() : base() { }
        public SearchConnectionManagerException(string message) : base(message) { }
        public SearchConnectionManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
