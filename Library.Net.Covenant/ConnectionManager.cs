using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Collections;
using Library.Io;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Covenant
{
    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullProfilesRequestEventArgs : EventArgs
    {
        public IEnumerable<QueryProfile> QueryProfiles { get; set; }
    }

    class PullProfilesEventArgs : EventArgs
    {
        public IEnumerable<Profile> Profiles { get; set; }
    }

    class PullMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Keywords { get; set; }
    }

    class PullMetadatasEventArgs : EventArgs
    {
        public string Keyword { get; set; }
        public IEnumerable<Metadata> Metadatas { get; set; }
    }

    class PullLocationsRequestEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullLocationsEventArgs : EventArgs
    {
        public IEnumerable<Location> Locations { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullProfilesRequestEventHandler(object sender, PullProfilesRequestEventArgs e);
    delegate void PullProfilesEventHandler(object sender, PullProfilesEventArgs e);

    delegate void PullMetadatasRequestEventHandler(object sender, PullMetadatasRequestEventArgs e);
    delegate void PullMetadatasEventHandler(object sender, PullMetadatasEventArgs e);

    delegate void PullLocationsRequestEventHandler(object sender, PullLocationsRequestEventArgs e);
    delegate void PullLocationsEventHandler(object sender, PullLocationsEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    [DataContract(Name = "ConnectDirection", Namespace = "http://Library/Net/Covenant")]
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

            ProfilesRequest = 5,
            Profiles = 6,

            MetadatasRequest = 7,
            Metadatas = 8,

            LocationsRequest = 9,
            Locations = 10,
        }

        private byte[] _mySessionId;
        private byte[] _otherSessionId;
        private ProtocolVersion _protocolVersion;
        private Connection _connection;
        private Node _baseNode;
        private Node _otherNode;
        private BufferManager _bufferManager;

        private ConnectDirection _direction;

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
        private const int _maxProfileRequestCount = 1024;
        private const int _maxProfileCount = 1024;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 1024;
        private const int _maxLocationRequestCount = 1024;
        private const int _maxLocationCount = 1024;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullProfilesRequestEventHandler PullProfilesRequestEvent;
        public event PullProfilesEventHandler PullProfilesEvent;

        public event PullMetadatasRequestEventHandler PullMetadatasRequestEvent;
        public event PullMetadatasEventHandler PullMetadatasEvent;

        public event PullLocationsRequestEventHandler PullLocationsRequestEvent;
        public event PullLocationsEventHandler PullLocationsEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(ProtocolVersion protocolVersion, Connection connection, byte[] mySessionId, Node baseNode, ConnectDirection direction, BufferManager bufferManager)
        {
            _protocolVersion = protocolVersion;
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
                    TimeSpan timeout = new TimeSpan(0, 0, 30);

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = new MemoryStream(_mySessionId))
                        {
                            _connection.Send(stream, timeout - stopwatch.Elapsed);
                        }

                        using (Stream stream = _connection.Receive(timeout - stopwatch.Elapsed))
                        {
                            if (stream.Length > 32) throw new ConnectionManagerException();

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

                        ThreadPool.QueueUserWorkItem(this.Pull);
                        _aliveTimer = new WatchTimer(this.AliveTimer, new TimeSpan(0, 0, 30));
                    }
                    else
                    {
                        throw new ConnectionManagerException();
                    }
                }
                catch (Exception ex)
                {
                    throw new ConnectionManagerException(ex.Message, ex);
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
                    throw new ConnectionManagerException(ex.Message, ex);
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
                throw new ConnectionManagerException();
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
                throw new ConnectionManagerException();
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
                throw new ConnectionManagerException();
            }
        }

        private void Pull(object state)
        {
            Thread.CurrentThread.Name = "ConnectionManager_Pull";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            try
            {
                Stopwatch sw = new Stopwatch();

                for (;;)
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    sw.Restart();

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        using (Stream stream = _connection.Receive(_receiveTimeSpan))
                        {
                            if (stream.Length == 0) continue;

                            byte type = (byte)stream.ReadByte();

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
                                    else if (type == (byte)SerializeId.ProfilesRequest)
                                    {
                                        var message = ProfilesRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullProfilesRequest(new PullProfilesRequestEventArgs()
                                        {
                                            QueryProfiles = message.QueryProfiles,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.Profiles)
                                    {
                                        var message = ProfilesMessage.Import(stream2, _bufferManager);

                                        this.OnPullProfiles(new PullProfilesEventArgs()
                                        {
                                            Profiles = message.Profiles,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MetadatasRequest)
                                    {
                                        var message = MetadatasRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullMetadatasRequest(new PullMetadatasRequestEventArgs()
                                        {
                                            Keywords = message.Keywords,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.Metadatas)
                                    {
                                        var message = MetadatasMessage.Import(stream2, _bufferManager);

                                        this.OnPullMetadatas(new PullMetadatasEventArgs()
                                        {
                                            Keyword = message.Keyword,
                                            Metadatas = message.Metadatas,
                                        });
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
                                }
                                catch (Exception)
                                {

                                }
                            }
                        }
                    }
                    else
                    {
                        throw new ConnectionManagerException();
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

        protected virtual void OnPullProfilesRequest(PullProfilesRequestEventArgs e)
        {
            if (this.PullProfilesRequestEvent != null)
            {
                this.PullProfilesRequestEvent(this, e);
            }
        }

        protected virtual void OnPullProfiles(PullProfilesEventArgs e)
        {
            if (this.PullProfilesEvent != null)
            {
                this.PullProfilesEvent(this, e);
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
                throw new ConnectionManagerException();
            }
        }

        public void PushProfilesRequest(IEnumerable<QueryProfile> queryProfiles)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.ProfilesRequest);
                    stream.Flush();

                    var message = new ProfilesRequestMessage(queryProfiles);

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
                throw new ConnectionManagerException();
            }
        }

        public void PushProfiles(IEnumerable<Profile> broadcastMetadats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Profiles);
                    stream.Flush();

                    var message = new ProfilesMessage(broadcastMetadats);

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
                throw new ConnectionManagerException();
            }
        }

        public void PushMetadatasRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MetadatasRequest);
                    stream.Flush();

                    var message = new MetadatasRequestMessage(signatures);

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
                throw new ConnectionManagerException();
            }
        }

        public void PushMetadatas(string keyword, IEnumerable<Metadata> Metadatas)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Metadatas);
                    stream.Flush();

                    var message = new MetadatasMessage(
                        keyword,
                        Metadatas);

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
                throw new ConnectionManagerException();
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
                throw new ConnectionManagerException();
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
                throw new ConnectionManagerException();
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
                throw new ConnectionManagerException();
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
                BufferStream bufferStream = new BufferStream(bufferManager);

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

        private sealed class ProfilesRequestMessage : ItemBase<ProfilesRequestMessage>
        {
            private enum SerializeId : byte
            {
                QueryProfile = 0,
            }

            private QueryProfileCollection _queryProfile;

            public ProfilesRequestMessage(IEnumerable<QueryProfile> queryProfiles)
            {
                if (queryProfiles != null) this.ProtectedQueryProfiles.AddRange(queryProfiles);
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
                        if (id == (byte)SerializeId.QueryProfile)
                        {
                            this.ProtectedQueryProfiles.Add(QueryProfile.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Signatures
                foreach (var value in this.QueryProfiles)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.QueryProfile, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<QueryProfile> _readOnlyQueryProfiles;

            public IEnumerable<QueryProfile> QueryProfiles
            {
                get
                {
                    if (_readOnlyQueryProfiles == null)
                        _readOnlyQueryProfiles = new ReadOnlyCollection<QueryProfile>(this.ProtectedQueryProfiles.ToArray());

                    return _readOnlyQueryProfiles;
                }
            }

            private QueryProfileCollection ProtectedQueryProfiles
            {
                get
                {
                    if (_queryProfile == null)
                        _queryProfile = new QueryProfileCollection(_maxProfileRequestCount);

                    return _queryProfile;
                }
            }
        }

        private sealed class ProfilesMessage : ItemBase<ProfilesMessage>
        {
            private enum SerializeId : byte
            {
                Profile = 0,
            }

            private LockedList<Profile> _profiles;

            public ProfilesMessage(IEnumerable<Profile> profiles)
            {
                if (profiles != null) this.ProtectedProfiles.AddRange(profiles);
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
                        if (id == (byte)SerializeId.Profile)
                        {
                            this.ProtectedProfiles.Add(Profile.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Profiles
                foreach (var value in this.Profiles)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Profile, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<Profile> _readOnlyProfiles;

            public IEnumerable<Profile> Profiles
            {
                get
                {
                    if (_readOnlyProfiles == null)
                        _readOnlyProfiles = new ReadOnlyCollection<Profile>(this.ProtectedProfiles.ToArray());

                    return _readOnlyProfiles;
                }
            }

            private LockedList<Profile> ProtectedProfiles
            {
                get
                {
                    if (_profiles == null)
                        _profiles = new LockedList<Profile>(_maxProfileCount);

                    return _profiles;
                }
            }
        }

        private sealed class MetadatasRequestMessage : ItemBase<MetadatasRequestMessage>
        {
            private enum SerializeId : byte
            {
                Keyword = 0,
            }

            private KeywordCollection _keywords;

            public MetadatasRequestMessage(IEnumerable<string> keywords)
            {
                if (keywords != null) this.ProtectedKeywords.AddRange(keywords);
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
                        if (id == (byte)SerializeId.Keyword)
                        {
                            this.ProtectedKeywords.Add(ItemUtilities.GetString(rangeStream));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Keywords
                foreach (var value in this.Keywords)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Keyword, value);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }

            private volatile ReadOnlyCollection<string> _readOnlyKeywords;

            public IEnumerable<string> Keywords
            {
                get
                {
                    if (_readOnlyKeywords == null)
                        _readOnlyKeywords = new ReadOnlyCollection<string>(this.ProtectedKeywords.ToArray());

                    return _readOnlyKeywords;
                }
            }

            private KeywordCollection ProtectedKeywords
            {
                get
                {
                    if (_keywords == null)
                        _keywords = new KeywordCollection(_maxMetadataRequestCount);

                    return _keywords;
                }
            }
        }

        private sealed class MetadatasMessage : ItemBase<MetadatasMessage>
        {
            private enum SerializeId : byte
            {
                Keyword = 0,
                Metadata = 1,
            }

            private string _keyword;
            private LockedList<Metadata> _warrants;

            public MetadatasMessage(string keyword, IEnumerable<Metadata> warrants)
            {
                this.Keyword = keyword;
                if (warrants != null) this.ProtectedMetadatas.AddRange(warrants);
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
                        if (id == (byte)SerializeId.Keyword)
                        {
                            this.Keyword = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Metadata)
                        {
                            this.ProtectedMetadatas.Add(Metadata.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Keyword
                if (this.Keyword != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Keyword, this.Keyword);
                }
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

            public string Keyword
            {
                get
                {
                    return _keyword;
                }
                private set
                {
                    _keyword = value;
                }
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
                    if (_warrants == null)
                        _warrants = new LockedList<Metadata>(_maxMetadataCount);

                    return _warrants;
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
                BufferStream bufferStream = new BufferStream(bufferManager);

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
                BufferStream bufferStream = new BufferStream(bufferManager);

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
    class ConnectionManagerException : ManagerException
    {
        public ConnectionManagerException() : base() { }
        public ConnectionManagerException(string message) : base(message) { }
        public ConnectionManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
