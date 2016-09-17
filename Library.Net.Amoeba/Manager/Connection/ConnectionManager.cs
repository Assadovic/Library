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
using Library.Utilities;

namespace Library.Net.Amoeba
{
    [Flags]
    [DataContract(Name = "ProtocolVersion")]
    enum ProtocolVersion
    {
        //[EnumMember(Value = "Version1")]
        //Version1 = 0x01,

        //[EnumMember(Value = "Version2")]
        //Version2 = 0x02,

        [EnumMember(Value = "Version3")]
        Version3 = 0x04,
    }

    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullBlocksLinkEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullBlocksRequestEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullBlockEventArgs : EventArgs
    {
        public Key Key { get; set; }
        public ArraySegment<byte> Value { get; set; }
    }

    class PullBroadcastMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Signatures { get; set; }
    }

    class PullBroadcastMetadatasEventArgs : EventArgs
    {
        public IEnumerable<BroadcastMetadata> BroadcastMetadatas { get; set; }
    }

    class PullUnicastMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<string> Signatures { get; set; }
    }

    class PullUnicastMetadatasEventArgs : EventArgs
    {
        public IEnumerable<UnicastMetadata> UnicastMetadatas { get; set; }
    }

    class PullMulticastMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<Tag> Tags { get; set; }
    }

    class PullMulticastMetadatasEventArgs : EventArgs
    {
        public IEnumerable<MulticastMetadata> MulticastMetadatas { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullBlocksLinkEventHandler(object sender, PullBlocksLinkEventArgs e);
    delegate void PullBlocksRequestEventHandler(object sender, PullBlocksRequestEventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);

    delegate void PullBroadcastMetadatasRequestEventHandler(object sender, PullBroadcastMetadatasRequestEventArgs e);
    delegate void PullBroadcastMetadatasEventHandler(object sender, PullBroadcastMetadatasEventArgs e);

    delegate void PullUnicastMetadatasRequestEventHandler(object sender, PullUnicastMetadatasRequestEventArgs e);
    delegate void PullUnicastMetadatasEventHandler(object sender, PullUnicastMetadatasEventArgs e);

    delegate void PullMulticastMetadatasRequestEventHandler(object sender, PullMulticastMetadatasRequestEventArgs e);
    delegate void PullMulticastMetadatasEventHandler(object sender, PullMulticastMetadatasEventArgs e);

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    [DataContract(Name = "ConnectDirection")]
    public enum ConnectDirection
    {
        [EnumMember(Value = "In")]
        In = 0,

        [EnumMember(Value = "Out")]
        Out = 1,
    }

    class ConnectionManager : ManagerBase
    {
        private enum SerializeId
        {
            Alive = 0,
            Cancel = 1,

            Ping = 2,
            Pong = 3,

            Nodes = 4,

            BlocksLink = 5,
            BlocksRequest = 6,
            Block = 7,

            BroadcastMetadatasRequest = 8,
            BroadcastMetadatas = 9,

            UnicastMetadatasRequest = 10,
            UnicastMetadatas = 11,

            MulticastMetadatasRequest = 12,
            MulticastMetadatas = 13,
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
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 8192;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 1024;

        public event PullNodesEventHandler PullNodesEvent;

        public event PullBlocksLinkEventHandler PullBlocksLinkEvent;
        public event PullBlocksRequestEventHandler PullBlocksRequestEvent;
        public event PullBlockEventHandler PullBlockEvent;

        public event PullBroadcastMetadatasRequestEventHandler PullBroadcastMetadatasRequestEvent;
        public event PullBroadcastMetadatasEventHandler PullBroadcastMetadatasEvent;

        public event PullUnicastMetadatasRequestEventHandler PullUnicastMetadatasRequestEvent;
        public event PullUnicastMetadatasEventHandler PullUnicastMetadatasEvent;

        public event PullMulticastMetadatasRequestEventHandler PullMulticastMetadatasRequestEvent;
        public event PullMulticastMetadatasEventHandler PullMulticastMetadatasEvent;

        public event PullCancelEventHandler PullCancelEvent;

        public event CloseEventHandler CloseEvent;

        public ConnectionManager(Connection connection, byte[] mySessionId, Node baseNode, ConnectDirection direction, BufferManager bufferManager)
        {
            _myProtocolVersion = ProtocolVersion.Version3;
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

                lock (_thisLock)
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

                lock (_thisLock)
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

                lock (_thisLock)
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

                lock (_thisLock)
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

                lock (_thisLock)
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

                lock (_thisLock)
                {
                    return _connection.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (_thisLock)
                {
                    return _connection.SentByteCount;
                }
            }
        }

        public TimeSpan ResponseTime
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (_thisLock)
                {
                    return _responseStopwatch.Elapsed;
                }
            }
        }

        public void Connect()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_thisLock)
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

                        if (_myProtocolVersion.HasFlag(ProtocolVersion.Version3))
                        {
                            xml.WriteStartElement("Amoeba");
                            xml.WriteAttributeString("Version", "3");
                            xml.WriteEndElement(); //Amoeba
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
                                if (xml.LocalName == "Amoeba")
                                {
                                    var version = xml.GetAttribute("Version");

                                    if (version == "3")
                                    {
                                        _otherProtocolVersion |= ProtocolVersion.Version3;
                                    }
                                }
                            }
                        }
                    }

                    _protocolVersion = _myProtocolVersion & _otherProtocolVersion;

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

                        Task.Factory.StartNew(this.PullThread, TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent);
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

            lock (_thisLock)
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

                    if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

                                        if (!CollectionUtils.Equals(buffer, _pingHash)) continue;

                                        _responseStopwatch.Stop();
                                    }
                                    else if (type == (byte)SerializeId.Nodes)
                                    {
                                        var message = NodesMessage.Import(stream2, _bufferManager);
                                        this.OnPullNodes(new PullNodesEventArgs() { Nodes = message.Nodes });
                                    }
                                    else if (type == (byte)SerializeId.BlocksLink)
                                    {
                                        var message = BlocksLinkMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlocksLink(new PullBlocksLinkEventArgs() { Keys = message.Keys });
                                    }
                                    else if (type == (byte)SerializeId.BlocksRequest)
                                    {
                                        var message = BlocksRequestMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlocksRequest(new PullBlocksRequestEventArgs() { Keys = message.Keys });
                                    }
                                    else if (type == (byte)SerializeId.Block)
                                    {
                                        var message = BlockMessage.Import(stream2, _bufferManager);
                                        this.OnPullBlock(new PullBlockEventArgs() { Key = message.Key, Value = message.Value });
                                    }
                                    else if (type == (byte)SerializeId.BroadcastMetadatasRequest)
                                    {
                                        var message = BroadcastMetadatasRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullBroadcastMetadatasRequest(new PullBroadcastMetadatasRequestEventArgs()
                                        {
                                            Signatures = message.Signatures,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.BroadcastMetadatas)
                                    {
                                        var message = BroadcastMetadatasMessage.Import(stream2, _bufferManager);

                                        this.OnPullBroadcastMetadatas(new PullBroadcastMetadatasEventArgs()
                                        {
                                            BroadcastMetadatas = message.BroadcastMetadatas,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.UnicastMetadatasRequest)
                                    {
                                        var message = UnicastMetadatasRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullUnicastMetadatasRequest(new PullUnicastMetadatasRequestEventArgs()
                                        {
                                            Signatures = message.Signatures,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.UnicastMetadatas)
                                    {
                                        var message = UnicastMetadatasMessage.Import(stream2, _bufferManager);

                                        this.OnPullUnicastMetadatas(new PullUnicastMetadatasEventArgs()
                                        {
                                            UnicastMetadatas = message.UnicastMetadatas,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MulticastMetadatasRequest)
                                    {
                                        var message = MulticastMetadatasRequestMessage.Import(stream2, _bufferManager);

                                        this.OnPullMulticastMetadatasRequest(new PullMulticastMetadatasRequestEventArgs()
                                        {
                                            Tags = message.Tags,
                                        });
                                    }
                                    else if (type == (byte)SerializeId.MulticastMetadatas)
                                    {
                                        var message = MulticastMetadatasMessage.Import(stream2, _bufferManager);

                                        this.OnPullMulticastMetadatas(new PullMulticastMetadatasEventArgs()
                                        {
                                            MulticastMetadatas = message.MulticastMetadatas,
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
            this.PullNodesEvent?.Invoke(this, e);
        }

        protected virtual void OnPullBlocksLink(PullBlocksLinkEventArgs e)
        {
            this.PullBlocksLinkEvent?.Invoke(this, e);
        }

        protected virtual void OnPullBlocksRequest(PullBlocksRequestEventArgs e)
        {
            this.PullBlocksRequestEvent?.Invoke(this, e);
        }

        protected virtual void OnPullBlock(PullBlockEventArgs e)
        {
            this.PullBlockEvent?.Invoke(this, e);
        }

        protected virtual void OnPullBroadcastMetadatasRequest(PullBroadcastMetadatasRequestEventArgs e)
        {
            this.PullBroadcastMetadatasRequestEvent?.Invoke(this, e);
        }

        protected virtual void OnPullBroadcastMetadatas(PullBroadcastMetadatasEventArgs e)
        {
            this.PullBroadcastMetadatasEvent?.Invoke(this, e);
        }

        protected virtual void OnPullUnicastMetadatasRequest(PullUnicastMetadatasRequestEventArgs e)
        {
            this.PullUnicastMetadatasRequestEvent?.Invoke(this, e);
        }

        protected virtual void OnPullUnicastMetadatas(PullUnicastMetadatasEventArgs e)
        {
            this.PullUnicastMetadatasEvent?.Invoke(this, e);
        }

        protected virtual void OnPullMulticastMetadatasRequest(PullMulticastMetadatasRequestEventArgs e)
        {
            this.PullMulticastMetadatasRequestEvent?.Invoke(this, e);
        }

        protected virtual void OnPullMulticastMetadatas(PullMulticastMetadatasEventArgs e)
        {
            this.PullMulticastMetadatasEvent?.Invoke(this, e);
        }

        protected virtual void OnPullCancel(EventArgs e)
        {
            this.PullCancelEvent?.Invoke(this, e);
        }

        protected virtual void OnClose(EventArgs e)
        {
            if (_onClose) return;
            _onClose = true;

            this.CloseEvent?.Invoke(this, e);
        }

        public void PushNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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

        public void PushBlocksLink(IEnumerable<Key> keys)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksLink);
                    stream.Flush();

                    var message = new BlocksLinkMessage(keys);

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

        public void PushBlocksRequest(IEnumerable<Key> keys)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BlocksRequest);
                    stream.Flush();

                    var message = new BlocksRequestMessage(keys);

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

        public void PushBlock(Key key, ArraySegment<byte> value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.Block);
                    stream.Flush();

                    var message = new BlockMessage(key, value);

                    stream = new UniteStream(stream, message.Export(_bufferManager));

                    var contexts = new List<InformationContext>();
                    contexts.Add(new InformationContext("IsCompress", false));

                    _connection.Send(stream, _sendTimeSpan, new Information(contexts));
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

        public void PushBroadcastMetadatasRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BroadcastMetadatasRequest);
                    stream.Flush();

                    var message = new BroadcastMetadatasRequestMessage(signatures);

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

        public void PushBroadcastMetadatas(IEnumerable<BroadcastMetadata> broadcastMetadats)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.BroadcastMetadatas);
                    stream.Flush();

                    var message = new BroadcastMetadatasMessage(broadcastMetadats);

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

        public void PushUnicastMetadatasRequest(IEnumerable<string> signatures)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.UnicastMetadatasRequest);
                    stream.Flush();

                    var message = new UnicastMetadatasRequestMessage(signatures);

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

        public void PushUnicastMetadatas(IEnumerable<UnicastMetadata> UnicastMetadatas)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.UnicastMetadatas);
                    stream.Flush();

                    var message = new UnicastMetadatasMessage(UnicastMetadatas);

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

        public void PushMulticastMetadatasRequest(IEnumerable<Tag> tags)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MulticastMetadatasRequest);
                    stream.Flush();

                    var message = new MulticastMetadatasRequestMessage(tags);

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

        public void PushMulticastMetadatas(IEnumerable<MulticastMetadata> multicastMetadatas)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
            {
                Stream stream = new BufferStream(_bufferManager);

                try
                {
                    stream.WriteByte((byte)SerializeId.MulticastMetadatas);
                    stream.Flush();

                    var message = new MulticastMetadatasMessage(multicastMetadatas);

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

            if (_protocolVersion.HasFlag(ProtocolVersion.Version3))
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
            private enum SerializeId
            {
                Node = 0,
            }

            private volatile NodeCollection _nodes;

            public NodesMessage(IEnumerable<Node> nodes)
            {
                if (nodes != null) this.ProtectedNodes.AddRange(nodes);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Node)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedNodes.Add(Node.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Nodes
                    foreach (var value in this.Nodes)
                    {
                        writer.Add((int)SerializeId.Node, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
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

        private sealed class BlocksLinkMessage : ItemBase<BlocksLinkMessage>
        {
            private enum SerializeId
            {
                Key = 0,
            }

            private volatile KeyCollection _keys;

            public BlocksLinkMessage(IEnumerable<Key> keys)
            {
                if (keys != null) this.ProtectedKeys.AddRange(keys);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Key)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Keys
                    foreach (var value in this.Keys)
                    {
                        writer.Add((int)SerializeId.Key, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
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

            private KeyCollection ProtectedKeys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection(_maxBlockLinkCount);

                    return _keys;
                }
            }
        }

        private sealed class BlocksRequestMessage : ItemBase<BlocksRequestMessage>
        {
            private enum SerializeId
            {
                Key = 0,
            }

            private volatile KeyCollection _keys;

            public BlocksRequestMessage(IEnumerable<Key> keys)
            {
                if (keys != null) this.ProtectedKeys.AddRange(keys);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Key)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedKeys.Add(Key.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Keys
                    foreach (var value in this.Keys)
                    {
                        writer.Add((int)SerializeId.Key, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
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

            private KeyCollection ProtectedKeys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection(_maxBlockRequestCount);

                    return _keys;
                }
            }
        }

        private sealed class BlockMessage : ItemBase<BlockMessage>
        {
            private enum SerializeId
            {
                Key = 0,
                Value = 1,
            }

            private volatile Key _key;
            private ArraySegment<byte> _value;

            private volatile object _thisLock;

            public BlockMessage(Key key, ArraySegment<byte> value)
            {
                this.Key = key;
                this.Value = value;
            }

            protected override void Initialize()
            {
                base.Initialize();

                _thisLock = new object();
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Key)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.Key = Key.Import(rangeStream, bufferManager);
                            }
                        }
                        else if (id == (int)SerializeId.Value)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                if (this.Value.Array != null)
                                {
                                    bufferManager.ReturnBuffer(this.Value.Array);
                                }

                                byte[] buffer = null;

                                try
                                {
                                    buffer = bufferManager.TakeBuffer((int)rangeStream.Length);
                                    rangeStream.Read(buffer, 0, (int)rangeStream.Length);
                                }
                                catch (Exception e)
                                {
                                    if (buffer != null)
                                    {
                                        bufferManager.ReturnBuffer(buffer);
                                    }

                                    throw e;
                                }

                                this.Value = new ArraySegment<byte>(buffer, 0, (int)rangeStream.Length);
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Key
                    if (this.Key != null)
                    {
                        writer.Add((int)SerializeId.Key, this.Key.Export(bufferManager));
                    }
                    // Value
                    if (this.Value.Array != null)
                    {
                        writer.Write((int)SerializeId.Value, this.Value.Array, this.Value.Offset, this.Value.Count);
                    }

                    return writer.GetStream();
                }
            }

            public Key Key
            {
                get
                {
                    return _key;
                }
                private set
                {
                    _key = value;
                }
            }

            public ArraySegment<byte> Value
            {
                get
                {
                    lock (_thisLock)
                    {
                        return _value;
                    }
                }
                private set
                {
                    lock (_thisLock)
                    {
                        _value = value;
                    }
                }
            }
        }

        private sealed class BroadcastMetadatasRequestMessage : ItemBase<BroadcastMetadatasRequestMessage>
        {
            private enum SerializeId
            {
                Signature = 0,
            }

            private volatile SignatureCollection _signatures;

            public BroadcastMetadatasRequestMessage(IEnumerable<string> signatures)
            {
                if (signatures != null) this.ProtectedSignatures.AddRange(signatures);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Signature)
                        {
                            this.ProtectedSignatures.Add(reader.GetString());
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Signatures
                    foreach (var value in this.Signatures)
                    {
                        writer.Write((int)SerializeId.Signature, value);
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<string> _readOnlySignatures;

            public IEnumerable<string> Signatures
            {
                get
                {
                    if (_readOnlySignatures == null)
                        _readOnlySignatures = new ReadOnlyCollection<string>(this.ProtectedSignatures.ToArray());

                    return _readOnlySignatures;
                }
            }

            private SignatureCollection ProtectedSignatures
            {
                get
                {
                    if (_signatures == null)
                        _signatures = new SignatureCollection(_maxMetadataRequestCount);

                    return _signatures;
                }
            }
        }

        private sealed class BroadcastMetadatasMessage : ItemBase<BroadcastMetadatasMessage>
        {
            private enum SerializeId
            {
                BroadcastMetadata = 0,
            }

            private volatile LockedList<BroadcastMetadata> _broadcastMetadatas;

            public BroadcastMetadatasMessage(IEnumerable<BroadcastMetadata> broadcastMetadatas)
            {
                if (broadcastMetadatas != null) this.ProtectedBroadcastMetadatas.AddRange(broadcastMetadatas);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.BroadcastMetadata)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedBroadcastMetadatas.Add(BroadcastMetadata.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // BroadcastMetadatas
                    foreach (var value in this.BroadcastMetadatas)
                    {
                        writer.Add((int)SerializeId.BroadcastMetadata, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<BroadcastMetadata> _readOnlyBroadcastMetadatas;

            public IEnumerable<BroadcastMetadata> BroadcastMetadatas
            {
                get
                {
                    if (_readOnlyBroadcastMetadatas == null)
                        _readOnlyBroadcastMetadatas = new ReadOnlyCollection<BroadcastMetadata>(this.ProtectedBroadcastMetadatas.ToArray());

                    return _readOnlyBroadcastMetadatas;
                }
            }

            private LockedList<BroadcastMetadata> ProtectedBroadcastMetadatas
            {
                get
                {
                    if (_broadcastMetadatas == null)
                        _broadcastMetadatas = new LockedList<BroadcastMetadata>(_maxMetadataCount);

                    return _broadcastMetadatas;
                }
            }
        }

        private sealed class UnicastMetadatasRequestMessage : ItemBase<UnicastMetadatasRequestMessage>
        {
            private enum SerializeId
            {
                Signature = 0,
            }

            private volatile SignatureCollection _signatures;

            public UnicastMetadatasRequestMessage(IEnumerable<string> signatures)
            {
                if (signatures != null) this.ProtectedSignatures.AddRange(signatures);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Signature)
                        {
                            this.ProtectedSignatures.Add(reader.GetString());
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Signatures
                    foreach (var value in this.Signatures)
                    {
                        writer.Write((int)SerializeId.Signature, value);
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<string> _readOnlySignatures;

            public IEnumerable<string> Signatures
            {
                get
                {
                    if (_readOnlySignatures == null)
                        _readOnlySignatures = new ReadOnlyCollection<string>(this.ProtectedSignatures.ToArray());

                    return _readOnlySignatures;
                }
            }

            private SignatureCollection ProtectedSignatures
            {
                get
                {
                    if (_signatures == null)
                        _signatures = new SignatureCollection(_maxMetadataRequestCount);

                    return _signatures;
                }
            }
        }

        private sealed class UnicastMetadatasMessage : ItemBase<UnicastMetadatasMessage>
        {
            private enum SerializeId
            {
                UnicastMetadata = 0,
            }

            private volatile LockedList<UnicastMetadata> _unicastMetadatas;

            public UnicastMetadatasMessage(IEnumerable<UnicastMetadata> unicastMetadatas)
            {
                if (unicastMetadatas != null) this.ProtectedUnicastMetadatas.AddRange(unicastMetadatas);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.UnicastMetadata)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedUnicastMetadatas.Add(UnicastMetadata.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // UnicastMetadatas
                    foreach (var value in this.UnicastMetadatas)
                    {
                        writer.Add((int)SerializeId.UnicastMetadata, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<UnicastMetadata> _readOnlyUnicastMetadatas;

            public IEnumerable<UnicastMetadata> UnicastMetadatas
            {
                get
                {
                    if (_readOnlyUnicastMetadatas == null)
                        _readOnlyUnicastMetadatas = new ReadOnlyCollection<UnicastMetadata>(this.ProtectedUnicastMetadatas.ToArray());

                    return _readOnlyUnicastMetadatas;
                }
            }

            private LockedList<UnicastMetadata> ProtectedUnicastMetadatas
            {
                get
                {
                    if (_unicastMetadatas == null)
                        _unicastMetadatas = new LockedList<UnicastMetadata>(_maxMetadataCount);

                    return _unicastMetadatas;
                }
            }
        }

        private sealed class MulticastMetadatasRequestMessage : ItemBase<MulticastMetadatasRequestMessage>
        {
            private enum SerializeId
            {
                Tag = 0,
            }

            private volatile TagCollection _tags;

            public MulticastMetadatasRequestMessage(IEnumerable<Tag> tags)
            {
                if (tags != null) this.ProtectedTags.AddRange(tags);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.Tag)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedTags.Add(Tag.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // Tags
                    foreach (var value in this.Tags)
                    {
                        writer.Add((int)SerializeId.Tag, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<Tag> _readOnlyTags;

            public IEnumerable<Tag> Tags
            {
                get
                {
                    if (_readOnlyTags == null)
                        _readOnlyTags = new ReadOnlyCollection<Tag>(this.ProtectedTags.ToArray());

                    return _readOnlyTags;
                }
            }

            [DataMember(Name = "Tags")]
            private TagCollection ProtectedTags
            {
                get
                {
                    if (_tags == null)
                        _tags = new TagCollection(_maxMetadataRequestCount);

                    return _tags;
                }
            }
        }

        private sealed class MulticastMetadatasMessage : ItemBase<MulticastMetadatasMessage>
        {
            private enum SerializeId
            {
                MulticastMetadata = 0,
            }

            private volatile LockedList<MulticastMetadata> _multicastMetadatas;

            public MulticastMetadatasMessage(IEnumerable<MulticastMetadata> multicastMetadatas)
            {
                if (multicastMetadatas != null) this.ProtectedMulticastMetadatas.AddRange(multicastMetadatas);
            }

            protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
            {
                using (var reader = new ItemStreamReader(stream, bufferManager))
                {
                    int id;

                    while ((id = reader.GetId()) != -1)
                    {
                        if (id == (int)SerializeId.MulticastMetadata)
                        {
                            using (var rangeStream = reader.GetStream())
                            {
                                this.ProtectedMulticastMetadatas.Add(MulticastMetadata.Import(rangeStream, bufferManager));
                            }
                        }
                    }
                }
            }

            protected override Stream Export(BufferManager bufferManager, int count)
            {
                using (var writer = new ItemStreamWriter(bufferManager))
                {
                    // MulticastMetadatas
                    foreach (var value in this.MulticastMetadatas)
                    {
                        writer.Add((int)SerializeId.MulticastMetadata, value.Export(bufferManager));
                    }

                    return writer.GetStream();
                }
            }

            private volatile ReadOnlyCollection<MulticastMetadata> _readOnlyMulticastMetadatas;

            public IEnumerable<MulticastMetadata> MulticastMetadatas
            {
                get
                {
                    if (_readOnlyMulticastMetadatas == null)
                        _readOnlyMulticastMetadatas = new ReadOnlyCollection<MulticastMetadata>(this.ProtectedMulticastMetadatas.ToArray());

                    return _readOnlyMulticastMetadatas;
                }
            }

            [DataMember(Name = "MulticastMetadatas")]
            private LockedList<MulticastMetadata> ProtectedMulticastMetadatas
            {
                get
                {
                    if (_multicastMetadatas == null)
                        _multicastMetadatas = new LockedList<MulticastMetadata>(_maxMetadataCount);

                    return _multicastMetadatas;
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
    }

    [Serializable]
    class ConnectionManagerException : ManagerException
    {
        public ConnectionManagerException() : base() { }
        public ConnectionManagerException(string message) : base(message) { }
        public ConnectionManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
