using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Net.Connections;
using Library.Security;

namespace Library.Net.Outopos
{
    public delegate IEnumerable<string> GetSignaturesEventHandler(object sender);
    public delegate IEnumerable<Tag> GetTagsEventHandler(object sender);

    class ConnectionsManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Random _random = new Random();

        private Kademlia<Node> _routeTable;

        private byte[] _mySessionId;

        private LockedList<ConnectionManager> _connectionManagers;
        private MessagesManager _messagesManager;

        private LockedHashDictionary<Node, List<Key>> _pushBlocksLinkDictionary = new LockedHashDictionary<Node, List<Key>>();
        private LockedHashDictionary<Node, List<Key>> _pushBlocksRequestDictionary = new LockedHashDictionary<Node, List<Key>>();
        private LockedHashDictionary<Node, List<string>> _pushBroadcastMetadatasRequestDictionary = new LockedHashDictionary<Node, List<string>>();
        private LockedHashDictionary<Node, List<string>> _pushUnicastMetadatasRequestDictionary = new LockedHashDictionary<Node, List<string>>();
        private LockedHashDictionary<Node, List<Tag>> _pushMulticastMetadatasRequestDictionary = new LockedHashDictionary<Node, List<Tag>>();

        private LockedHashDictionary<Node, Queue<Key>> _diffusionBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();
        private LockedHashDictionary<Node, Queue<Key>> _uploadBlocksDictionary = new LockedHashDictionary<Node, Queue<Key>>();

        private WatchTimer _refreshTimer;

        private LockedList<Node> _creatingNodes;

        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;

        private VolatileHashSet<string> _succeededUris;

        private VolatileHashSet<Key> _downloadBlocks;
        private VolatileHashSet<string> _pushBroadcastMetadatasRequestList;
        private VolatileHashSet<string> _pushUnicastMetadatasRequestList;
        private VolatileHashSet<Tag> _pushMulticastMetadatasRequestList;

        private LockedHashDictionary<string, DateTime> _broadcastMetadataLastAccessTimes = new LockedHashDictionary<string, DateTime>();
        private LockedHashDictionary<string, DateTime> _unicastMetadataLastAccessTimes = new LockedHashDictionary<string, DateTime>();
        private LockedHashDictionary<Tag, DateTime> _multicastMetadataLastAccessTimes = new LockedHashDictionary<Tag, DateTime>();

        private Thread _connectionsManagerThread;
        private List<Thread> _createConnectionThreads = new List<Thread>();
        private List<Thread> _acceptConnectionThreads = new List<Thread>();

        private volatile ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount;
        private long _sentByteCount;

        private readonly SafeInteger _pushNodeCount = new SafeInteger();
        private readonly SafeInteger _pushBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pushBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pushBlockCount = new SafeInteger();
        private readonly SafeInteger _pushMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pushMetadataCount = new SafeInteger();

        private readonly SafeInteger _pullNodeCount = new SafeInteger();
        private readonly SafeInteger _pullBlockLinkCount = new SafeInteger();
        private readonly SafeInteger _pullBlockRequestCount = new SafeInteger();
        private readonly SafeInteger _pullBlockCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataCount = new SafeInteger();

        private VolatileHashSet<Key> _relayBlocks;
        private readonly SafeInteger _relayBlockCount = new SafeInteger();

        private readonly SafeInteger _connectConnectionCount = new SafeInteger();
        private readonly SafeInteger _acceptConnectionCount = new SafeInteger();

        private GetSignaturesEventHandler _getLockSignaturesEvent;
        private GetTagsEventHandler _getLockTagsEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxNodeCount = 128;
        private const int _maxBlockLinkCount = 8192;
        private const int _maxBlockRequestCount = 2048;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 1024;

        private const int _routeTableMinCount = 100;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha256;

        //#if DEBUG
        //        private const int _downloadingConnectionCountLowerLimit = 0;
        //        private const int _uploadingConnectionCountLowerLimit = 0;
        //        private const int _diffusionConnectionCountLowerLimit = 3;
        //#else
        private const int _downloadingConnectionCountLowerLimit = 3;
        private const int _uploadingConnectionCountLowerLimit = 3;
        private const int _diffusionConnectionCountLowerLimit = 12;
        //#endif

        public ConnectionsManager(ClientManager clientManager, ServerManager serverManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _clientManager = clientManager;
            _serverManager = serverManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _routeTable = new Kademlia<Node>(512, 20);

            _connectionManagers = new LockedList<ConnectionManager>();

            _messagesManager = new MessagesManager();
            _messagesManager.GetLockNodesEvent = (object sender) =>
            {
                lock (this.ThisLock)
                {
                    return _connectionManagers.Select(n => n.Node).ToArray();
                }
            };

            _creatingNodes = new LockedList<Node>();

            _waitingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 0, 30));
            _cuttingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileHashSet<Node>(new TimeSpan(0, 30, 0));

            _succeededUris = new VolatileHashSet<string>(new TimeSpan(1, 0, 0));

            _downloadBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pushBroadcastMetadatasRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));
            _pushUnicastMetadatasRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));
            _pushMulticastMetadatasRequestList = new VolatileHashSet<Tag>(new TimeSpan(0, 3, 0));

            _relayBlocks = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 5));
        }

        private void RefreshTimer()
        {
            _waitingNodes.TrimExcess();
            _cuttingNodes.TrimExcess();
            _removeNodes.TrimExcess();

            _succeededUris.TrimExcess();

            _downloadBlocks.TrimExcess();
            _pushBroadcastMetadatasRequestList.TrimExcess();
            _pushUnicastMetadatasRequestList.TrimExcess();
            _pushMulticastMetadatasRequestList.TrimExcess();

            _relayBlocks.TrimExcess();
        }

        public GetSignaturesEventHandler GetLockSignaturesEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockSignaturesEvent = value;
                }
            }
        }

        public GetTagsEventHandler GetLockTagsEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _getLockTagsEvent = value;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _routeTable.BaseNode;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _routeTable.ToArray();
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _settings.ConnectionCountLimit;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _settings.ConnectionCountLimit = value;
                }
            }
        }

        public int BandwidthLimit
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return (_bandwidthLimit.In + _bandwidthLimit.Out) / 2;
                }
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    _bandwidthLimit.In = value;
                    _bandwidthLimit.Out = value;
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    var list = new List<Information>();

                    foreach (var connectionManager in _connectionManagers.ToArray())
                    {
                        var contexts = new List<InformationContext>();

                        var messageManager = _messagesManager[connectionManager.Node];

                        contexts.Add(new InformationContext("Id", messageManager.Id));
                        contexts.Add(new InformationContext("Node", connectionManager.Node));
                        contexts.Add(new InformationContext("Uri", _nodeToUri[connectionManager.Node]));
                        contexts.Add(new InformationContext("Priority", (long)messageManager.Priority));
                        contexts.Add(new InformationContext("ReceivedByteCount", (long)messageManager.ReceivedByteCount + connectionManager.ReceivedByteCount));
                        contexts.Add(new InformationContext("SentByteCount", (long)messageManager.SentByteCount + connectionManager.SentByteCount));
                        contexts.Add(new InformationContext("Direction", connectionManager.Direction));

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public Information Information
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    var contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("PushNodeCount", (long)_pushNodeCount));
                    contexts.Add(new InformationContext("PushBlockLinkCount", (long)_pushBlockLinkCount));
                    contexts.Add(new InformationContext("PushBlockRequestCount", (long)_pushBlockRequestCount));
                    contexts.Add(new InformationContext("PushBlockCount", (long)_pushBlockCount));
                    contexts.Add(new InformationContext("PushMetadataRequestCount", (long)_pushMetadataRequestCount));
                    contexts.Add(new InformationContext("PushMetadataCount", (long)_pushMetadataCount));

                    contexts.Add(new InformationContext("PullNodeCount", (long)_pullNodeCount));
                    contexts.Add(new InformationContext("PullBlockLinkCount", (long)_pullBlockLinkCount));
                    contexts.Add(new InformationContext("PullBlockRequestCount", (long)_pullBlockRequestCount));
                    contexts.Add(new InformationContext("PullBlockCount", (long)_pullBlockCount));
                    contexts.Add(new InformationContext("PullMetadataRequestCount", (long)_pullMetadataRequestCount));
                    contexts.Add(new InformationContext("PullMetadataCount", (long)_pullMetadataCount));

                    contexts.Add(new InformationContext("CreateConnectionCount", (long)_connectConnectionCount));
                    contexts.Add(new InformationContext("AcceptConnectionCount", (long)_acceptConnectionCount));

                    contexts.Add(new InformationContext("OtherNodeCount", _routeTable.Count));

                    {
                        var nodes = new HashSet<Node>();

                        foreach (var connectionManager in _connectionManagers)
                        {
                            nodes.Add(connectionManager.Node);
                        }

                        contexts.Add(new InformationContext("SurroundingNodeCount", nodes.Count));
                    }

                    contexts.Add(new InformationContext("BlockCount", _cacheManager.Count));
                    contexts.Add(new InformationContext("RelayBlockCount", (long)_relayBlockCount));

                    contexts.Add(new InformationContext("MetadataCount", _settings.MetadataManager.Count));

                    return new Information(contexts);
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _receivedByteCount + _connectionManagers.Sum(n => n.ReceivedByteCount);
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (this.ThisLock)
                {
                    return _sentByteCount + _connectionManagers.Sum(n => n.SentByteCount);
                }
            }
        }

        protected virtual IEnumerable<string> OnLockSignaturesEvent()
        {
            return _getLockSignaturesEvent?.Invoke(this);
        }

        protected virtual IEnumerable<Tag> OnLockTagsEvent()
        {
            return _getLockTagsEvent?.Invoke(this);
        }

        private static bool Check(Node node)
        {
            return !(node == null
                || node.Id == null || node.Id.Length == 0);
        }

        private static bool Check(Key key)
        {
            return !(key == null
                || key.Hash == null || key.Hash.Length == 0
                || key.HashAlgorithm != HashAlgorithm.Sha256);
        }

        private static bool Check(Tag tag)
        {
            return !(tag == null
                || tag.Name == null
                || tag.Id == null || tag.Id.Length == 0);
        }

        private void UpdateSessionId()
        {
            lock (this.ThisLock)
            {
                _mySessionId = new byte[32];

                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(_mySessionId);
                }
            }
        }

        private void RemoveNode(Node node)
        {
            lock (this.ThisLock)
            {
                _removeNodes.Add(node);
                _cuttingNodes.Remove(node);

                if (_routeTable.Count > _routeTableMinCount)
                {
                    _routeTable.Remove(node);
                }
            }
        }

        private double GetPriority(Node node)
        {
            const int average = 256;

            lock (this.ThisLock)
            {
                var priority = (long)_messagesManager[node].Priority;

                return ((double)(priority + average)) / (average * 2);
            }
        }

        private void AddConnectionManager(ConnectionManager connectionManager, string uri)
        {
            lock (this.ThisLock)
            {
                if (CollectionUtilities.Equals(connectionManager.Node.Id, this.BaseNode.Id)
                    || _connectionManagers.Any(n => CollectionUtilities.Equals(n.Node.Id, connectionManager.Node.Id)))
                {
                    connectionManager.Dispose();
                    return;
                }

                if (_connectionManagers.Count >= this.ConnectionCountLimit)
                {
                    connectionManager.Dispose();
                    return;
                }

                Debug.WriteLine("ConnectionManager: Connect");

                connectionManager.PullNodesEvent += this.connectionManager_NodesEvent;
                connectionManager.PullBlocksLinkEvent += this.connectionManager_BlocksLinkEvent;
                connectionManager.PullBlocksRequestEvent += this.connectionManager_BlocksRequestEvent;
                connectionManager.PullBlockEvent += this.connectionManager_BlockEvent;
                connectionManager.PullBroadcastMetadatasRequestEvent += this.connectionManager_PullBroadcastMetadatasRequestEvent;
                connectionManager.PullBroadcastMetadatasEvent += this.connectionManager_PullBroadcastMetadatasEvent;
                connectionManager.PullUnicastMetadatasRequestEvent += this.connectionManager_PullUnicastMetadatasRequestEvent;
                connectionManager.PullUnicastMetadatasEvent += this.connectionManager_PullUnicastMetadatasEvent;
                connectionManager.PullMulticastMetadatasRequestEvent += this.connectionManager_PullMulticastMetadatasRequestEvent;
                connectionManager.PullMulticastMetadatasEvent += this.connectionManager_PullMulticastMetadatasEvent;
                connectionManager.PullCancelEvent += this.connectionManager_PullCancelEvent;
                connectionManager.CloseEvent += this.connectionManager_CloseEvent;

                _nodeToUri.Add(connectionManager.Node, uri);
                _connectionManagers.Add(connectionManager);

                {
                    var tempMessageManager = _messagesManager[connectionManager.Node];

                    if (tempMessageManager.SessionId != null
                        && !CollectionUtilities.Equals(tempMessageManager.SessionId, connectionManager.SesstionId))
                    {
                        _messagesManager.Remove(connectionManager.Node);
                    }
                }

                var messageManager = _messagesManager[connectionManager.Node];
                messageManager.SessionId = connectionManager.SesstionId;
                messageManager.LastPullTime = DateTime.UtcNow;

                Task.Factory.StartNew(this.ConnectionManagerThread, connectionManager, TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent);
            }
        }

        private void RemoveConnectionManager(ConnectionManager connectionManager)
        {
            lock (this.ThisLock)
            {
                lock (_connectionManagers.ThisLock)
                {
                    try
                    {
                        if (_connectionManagers.Contains(connectionManager))
                        {
                            Debug.WriteLine("ConnectionManager: Close");

                            _sentByteCount += connectionManager.SentByteCount;
                            _receivedByteCount += connectionManager.ReceivedByteCount;

                            var messageManager = _messagesManager[connectionManager.Node];
                            messageManager.SentByteCount.Add(connectionManager.SentByteCount);
                            messageManager.ReceivedByteCount.Add(connectionManager.ReceivedByteCount);

                            _nodeToUri.Remove(connectionManager.Node);
                            _connectionManagers.Remove(connectionManager);

                            connectionManager.Dispose();
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        private void CreateConnectionThread()
        {
            for (;;)
            {
                if (this.State == ManagerState.Stop) return;
                Thread.Sleep(1000);

                // 接続数を制限する。
                {
                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.Out);
                    }

                    if (connectionCount >= (this.ConnectionCountLimit / 2))
                    {
                        continue;
                    }
                }

                Node node = null;

                lock (this.ThisLock)
                {
                    node = _cuttingNodes
                        .ToArray()
                        .Where(n => !_connectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
                            && !_creatingNodes.Contains(n)
                            && !_waitingNodes.Contains(n))
                        .Randomize()
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_connectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
                                && !_creatingNodes.Contains(n)
                                && !_waitingNodes.Contains(n))
                            .Randomize()
                            .FirstOrDefault();
                    }

                    if (node == null) continue;

                    _creatingNodes.Add(node);
                    _waitingNodes.Add(node);
                }

                try
                {
                    var uris = new HashSet<string>();
                    uris.UnionWith(node.Uris.Take(12));

                    if (uris.Count == 0)
                    {
                        lock (this.ThisLock)
                        {
                            _removeNodes.Remove(node);
                            _cuttingNodes.Remove(node);
                            _routeTable.Remove(node);
                        }

                        continue;
                    }

                    foreach (var uri in uris.Randomize())
                    {
                        if (this.State == ManagerState.Stop) return;

                        var connection = _clientManager.CreateConnection(uri, _bandwidthLimit);

                        if (connection != null)
                        {
                            var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.Out, _bufferManager);

                            try
                            {
                                connectionManager.Connect();
                                if (!ConnectionsManager.Check(connectionManager.Node)) throw new ArgumentException();

                                _succeededUris.Add(uri);

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != connectionManager.Node)
                                    {
                                        this.RemoveNode(connectionManager.Node);
                                    }

                                    if (connectionManager.Node.Uris.Count() != 0)
                                    {
                                        _routeTable.Live(connectionManager.Node);
                                    }
                                }

                                _connectConnectionCount.Increment();

                                this.AddConnectionManager(connectionManager, uri);

                                goto End;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e);

                                connectionManager.Dispose();
                            }
                        }
                    }

                    this.RemoveNode(node);
                    End:;
                }
                finally
                {
                    _creatingNodes.Remove(node);
                }
            }
        }

        private void AcceptConnectionThread()
        {
            for (;;)
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                // 接続数を制限する。
                {
                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count(n => n.Direction == ConnectDirection.In);
                    }

                    if (connectionCount >= ((this.ConnectionCountLimit + 1) / 2))
                    {
                        continue;
                    }
                }

                string uri;
                var connection = _serverManager.AcceptConnection(out uri, _bandwidthLimit);

                if (connection != null)
                {
                    var connectionManager = new ConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.In, _bufferManager);

                    try
                    {
                        connectionManager.Connect();
                        if (!ConnectionsManager.Check(connectionManager.Node) || _removeNodes.Contains(connectionManager.Node)) throw new ArgumentException();

                        lock (this.ThisLock)
                        {
                            if (connectionManager.Node.Uris.Count() != 0)
                            {
                                _routeTable.Add(connectionManager.Node);
                            }

                            _cuttingNodes.Remove(connectionManager.Node);
                        }

                        this.AddConnectionManager(connectionManager, uri);

                        _acceptConnectionCount.Increment();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);

                        connectionManager.Dispose();
                    }
                }
            }
        }

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public long Priority { get; set; }
            public DateTime LastPullTime { get; set; }
        }

        private volatile bool _refreshThreadRunning;

        private void ConnectionsManagerThread()
        {
            var connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            var refreshStopwatch = new Stopwatch();

            var pushBlockDiffusionStopwatch = new Stopwatch();
            pushBlockDiffusionStopwatch.Start();
            var pushBlockUploadStopwatch = new Stopwatch();
            pushBlockUploadStopwatch.Start();
            var pushBlockDownloadStopwatch = new Stopwatch();
            pushBlockDownloadStopwatch.Start();

            var pushMetadataUploadStopwatch = new Stopwatch();
            pushMetadataUploadStopwatch.Start();
            var pushMetadataDownloadStopwatch = new Stopwatch();
            pushMetadataDownloadStopwatch.Start();

            for (;;)
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _connectionManagers.Count;
                }

                if (connectionCount > ((this.ConnectionCountLimit / 3) * 1)
                    && connectionCheckStopwatch.Elapsed.TotalMinutes >= 5)
                {
                    connectionCheckStopwatch.Restart();

                    var nodeSortItems = new List<NodeSortItem>();

                    lock (this.ThisLock)
                    {
                        foreach (var connectionManager in _connectionManagers)
                        {
                            nodeSortItems.Add(new NodeSortItem()
                            {
                                Node = connectionManager.Node,
                                Priority = _messagesManager[connectionManager.Node].Priority,
                                LastPullTime = _messagesManager[connectionManager.Node].LastPullTime,
                            });
                        }
                    }

                    nodeSortItems.Sort((x, y) =>
                    {
                        int c = x.Priority.CompareTo(y.Priority);
                        if (c != 0) return c;

                        return x.LastPullTime.CompareTo(y.LastPullTime);
                    });

                    foreach (var node in nodeSortItems.Select(n => n.Node).Take(1))
                    {
                        ConnectionManager connectionManager = null;

                        lock (this.ThisLock)
                        {
                            connectionManager = _connectionManagers.FirstOrDefault(n => n.Node == node);
                        }

                        if (connectionManager != null)
                        {
                            try
                            {
                                lock (this.ThisLock)
                                {
                                    this.RemoveNode(connectionManager.Node);
                                }

                                connectionManager.PushCancel();

                                Debug.WriteLine("ConnectionManager: Push Cancel");
                            }
                            catch (Exception)
                            {

                            }

                            this.RemoveConnectionManager(connectionManager);
                        }
                    }
                }

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    refreshStopwatch.Restart();

                    // トラストにより必要なMetadataを選択し、不要なMetadataを削除する。
                    Task.Run(() =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var lockSignatures = this.OnLockSignaturesEvent();
                            if (lockSignatures == null) return;

                            var lockTags = this.OnLockTagsEvent();
                            if (lockTags == null) return;

                            // Broadcast
                            {
                                // Signature
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.MetadataManager.GetBroadcastSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _broadcastMetadataLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveBroadcastSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.MetadataManager.GetBroadcastSignatures());

                                    foreach (var signature in _broadcastMetadataLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _broadcastMetadataLastAccessTimes.Remove(signature);
                                    }
                                }
                            }

                            // Unicast
                            {
                                // Signature
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.MetadataManager.GetUnicastSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _unicastMetadataLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveUnicastSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.MetadataManager.GetUnicastSignatures());

                                    foreach (var signature in _unicastMetadataLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _unicastMetadataLastAccessTimes.Remove(signature);
                                    }
                                }
                            }

                            // Multicast
                            {
                                // Tag
                                {
                                    var removeTags = new HashSet<Tag>();
                                    removeTags.UnionWith(_settings.MetadataManager.GetMulticastTags());
                                    removeTags.ExceptWith(lockTags);

                                    var sortList = removeTags
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _multicastMetadataLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.MetadataManager.RemoveMulticastTags(sortList.Take(sortList.Count - 1024));

                                    var liveTags = new HashSet<Tag>(_settings.MetadataManager.GetMulticastTags());

                                    foreach (var tag in _multicastMetadataLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveTags.Contains(tag)) continue;

                                        _multicastMetadataLastAccessTimes.Remove(tag);
                                    }
                                }
                            }

                            // Unicast
                            {
                                var trustSignature = new HashSet<string>(lockSignatures);

                                {
                                    var now = DateTime.UtcNow;

                                    var removeUnicastMetadatas = new HashSet<UnicastMetadata>();

                                    foreach (var targetSignature in _settings.MetadataManager.GetUnicastSignatures())
                                    {
                                        var trustMetadatas = new Dictionary<string, List<UnicastMetadata>>();
                                        var untrustMetadatas = new Dictionary<string, List<UnicastMetadata>>();

                                        foreach (var metadata in _settings.MetadataManager.GetUnicastMetadatas(targetSignature))
                                        {
                                            var signature = metadata.Certificate.ToString();

                                            if (trustSignature.Contains(signature))
                                            {
                                                List<UnicastMetadata> list;

                                                if (!trustMetadatas.TryGetValue(signature, out list))
                                                {
                                                    list = new List<UnicastMetadata>();
                                                    trustMetadatas[signature] = list;
                                                }

                                                list.Add(metadata);
                                            }
                                            else
                                            {
                                                List<UnicastMetadata> list;

                                                if (!untrustMetadatas.TryGetValue(signature, out list))
                                                {
                                                    list = new List<UnicastMetadata>();
                                                    untrustMetadatas[signature] = list;
                                                }

                                                list.Add(metadata);
                                            }
                                        }

                                        removeUnicastMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(256).SelectMany(n => n.Value));

                                        foreach (var list in CollectionUtilities.Unite(trustMetadatas.Values, untrustMetadatas.Values))
                                        {
                                            if (list.Count <= 32) continue;

                                            list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                            removeUnicastMetadatas.UnionWith(list.Take(list.Count - 32));
                                        }
                                    }

                                    foreach (var metadata in removeUnicastMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
                                    }
                                }
                            }

                            // Multicast
                            {
                                var trustSignature = new HashSet<string>(lockSignatures);

                                {
                                    var now = DateTime.UtcNow;

                                    var removeMulticastMetadatas = new HashSet<MulticastMetadata>();

                                    foreach (var tag in _settings.MetadataManager.GetMulticastTags())
                                    {
                                        var trustMetadatas = new Dictionary<string, List<MulticastMetadata>>();
                                        var untrustMetadatas = new Dictionary<string, List<MulticastMetadata>>();

                                        foreach (var metadata in _settings.MetadataManager.GetMulticastMetadatas(tag))
                                        {
                                            var signature = metadata.Certificate.ToString();

                                            if (trustSignature.Contains(signature))
                                            {
                                                List<MulticastMetadata> list;

                                                if (!trustMetadatas.TryGetValue(signature, out list))
                                                {
                                                    list = new List<MulticastMetadata>();
                                                    trustMetadatas[signature] = list;
                                                }

                                                list.Add(metadata);
                                            }
                                            else
                                            {
                                                List<MulticastMetadata> list;

                                                if (!untrustMetadatas.TryGetValue(signature, out list))
                                                {
                                                    list = new List<MulticastMetadata>();
                                                    untrustMetadatas[signature] = list;
                                                }

                                                list.Add(metadata);
                                            }
                                        }

                                        removeMulticastMetadatas.UnionWith(untrustMetadatas.Randomize().Skip(256).SelectMany(n => n.Value));

                                        foreach (var list in CollectionUtilities.Unite(trustMetadatas.Values, untrustMetadatas.Values))
                                        {
                                            if (list.Count <= 32) continue;

                                            list.Sort((x, y) => x.CreationTime.CompareTo(y.CreationTime));
                                            removeMulticastMetadatas.UnionWith(list.Take(list.Count - 32));
                                        }
                                    }

                                    foreach (var metadata in removeMulticastMetadatas)
                                    {
                                        _settings.MetadataManager.RemoveMetadata(metadata);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                        finally
                        {
                            _refreshThreadRunning = false;
                        }
                    });
                }

                // 拡散アップロード
                if (connectionCount > _diffusionConnectionCountLowerLimit
                    && pushBlockDiffusionStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDiffusionStopwatch.Restart();

                    // 拡散アップロードするブロック数を10000以下に抑える。
                    lock (this.ThisLock)
                    {
                        lock (_settings.DiffusionBlocksRequest.ThisLock)
                        {
                            if (_settings.DiffusionBlocksRequest.Count > 10000)
                            {
                                foreach (var key in _settings.DiffusionBlocksRequest.ToArray().Randomize()
                                    .Take(_settings.DiffusionBlocksRequest.Count - 10000).ToList())
                                {
                                    _settings.DiffusionBlocksRequest.Remove(key);
                                }
                            }
                        }
                    }

                    // 存在しないブロックのKeyをRemoveする。
                    lock (this.ThisLock)
                    {
                        lock (_settings.DiffusionBlocksRequest.ThisLock)
                        {
                            foreach (var key in _cacheManager.ExceptFrom(_settings.DiffusionBlocksRequest.ToArray()).ToArray())
                            {
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }

                        lock (_settings.UploadBlocksRequest.ThisLock)
                        {
                            foreach (var key in _cacheManager.ExceptFrom(_settings.UploadBlocksRequest.ToArray()).ToArray())
                            {
                                _settings.UploadBlocksRequest.Remove(key);
                            }
                        }
                    }

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var diffusionBlocksList = new List<Key>();

                    {
                        {
                            var array = _settings.UploadBlocksRequest.ToArray();
                            _random.Shuffle(array);

                            int count = 256;

                            for (int i = 0; i < count && i < array.Length; i++)
                            {
                                diffusionBlocksList.Add(array[i]);
                            }
                        }

                        {
                            var array = _settings.DiffusionBlocksRequest.ToArray();
                            _random.Shuffle(array);

                            int count = 256;

                            for (int i = 0; i < count && i < array.Length; i++)
                            {
                                diffusionBlocksList.Add(array[i]);
                            }
                        }
                    }

                    _random.Shuffle(diffusionBlocksList);

                    {
                        var diffusionBlocksDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var key in diffusionBlocksList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(key.Hash, baseNode.Id, otherNodes, 1))
                                {
                                    requestNodes.Add(node);
                                }

                                if (requestNodes.Count == 0)
                                {
                                    _settings.UploadBlocksRequest.Remove(key);
                                    _settings.DiffusionBlocksRequest.Remove(key);

                                    continue;
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Key> collection;

                                    if (!diffusionBlocksDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Key>();
                                        diffusionBlocksDictionary[requestNodes[i]] = collection;
                                    }

                                    collection.Add(key);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_diffusionBlocksDictionary.ThisLock)
                        {
                            _diffusionBlocksDictionary.Clear();

                            foreach (var pair in diffusionBlocksDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _diffusionBlocksDictionary.Add(node, new Queue<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // アップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushBlockUploadStopwatch.Elapsed.TotalSeconds >= 10)
                {
                    pushBlockUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    {
                        var uploadBlocksDictionary = new Dictionary<Node, List<Key>>();

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            uploadBlocksDictionary.Add(node, _cacheManager.IntersectFrom(messageManager.PullBlocksRequest.ToArray().Randomize()).Take(128).ToList());
                        }

                        lock (_uploadBlocksDictionary.ThisLock)
                        {
                            _uploadBlocksDictionary.Clear();

                            foreach (var pair in uploadBlocksDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _uploadBlocksDictionary.Add(node, new Queue<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // ダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushBlockDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushBlockDownloadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var pushBlocksLinkList = new List<Key>();
                    var pushBlocksRequestList = new List<Key>();

                    {
                        {
                            var array = _cacheManager.ToArray();
                            _random.Shuffle(array);

                            int count = _maxBlockLinkCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                pushBlocksLinkList.Add(array[i]);

                                count--;
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullBlocksLink.ToArray();
                                _random.Shuffle(array);

                                var count = (int)(_maxBlockLinkCount * ((double)8 / otherNodes.Count));

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushBlocksLinkList.Add(array[i]);

                                    count--;
                                }
                            }
                        }

                        {
                            var array = _cacheManager.ExceptFrom(_downloadBlocks.ToArray()).ToArray();
                            _random.Shuffle(array);

                            int count = _maxBlockRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                pushBlocksRequestList.Add(array[i]);

                                count--;
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = _cacheManager.ExceptFrom(messageManager.PullBlocksRequest.ToArray()).ToArray();
                                _random.Shuffle(array);

                                var count = (int)(_maxBlockRequestCount * ((double)8 / otherNodes.Count));

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushBlocksRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }
                    }

                    _random.Shuffle(pushBlocksLinkList);
                    _random.Shuffle(pushBlocksRequestList);

                    {
                        var pushBlocksLinkDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var key in pushBlocksLinkList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(key.Hash, otherNodes, 1))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Key> collection;

                                    if (!pushBlocksLinkDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Key>();
                                        pushBlocksLinkDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxBlockLinkCount)
                                    {
                                        collection.Add(key);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushBlocksLinkDictionary.ThisLock)
                        {
                            _pushBlocksLinkDictionary.Clear();

                            foreach (var pair in pushBlocksLinkDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBlocksLinkDictionary.Add(node, new List<Key>(targets.Randomize()));
                            }
                        }
                    }

                    {
                        var pushBlocksRequestDictionary = new Dictionary<Node, HashSet<Key>>();

                        foreach (var key in pushBlocksRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(key.Hash, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                foreach (var pair in messageManagers)
                                {
                                    var node = pair.Key;
                                    var messageManager = pair.Value;

                                    if (messageManager.PullBlocksLink.Contains(key))
                                    {
                                        requestNodes.Add(node);
                                    }
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Key> collection;

                                    if (!pushBlocksRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Key>();
                                        pushBlocksRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxBlockRequestCount)
                                    {
                                        collection.Add(key);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushBlocksRequestDictionary.ThisLock)
                        {
                            _pushBlocksRequestDictionary.Clear();

                            foreach (var pair in pushBlocksRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBlocksRequestDictionary.Add(node, new List<Key>(targets.Randomize()));
                            }
                        }
                    }
                }

                // Metadataのアップロード
                if (connectionCount >= _uploadingConnectionCountLowerLimit
                    && pushMetadataUploadStopwatch.Elapsed.TotalMinutes >= 3)
                {
                    pushMetadataUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    // Broadcast
                    foreach (var signature in _settings.MetadataManager.GetBroadcastSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), otherNodes, 2))
                            {
                                requestNodes.Add(node);
                            }

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                messageManagers[requestNodes[i]].PullBroadcastMetadatasRequest.Add(signature);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    // Unicast
                    foreach (var signature in _settings.MetadataManager.GetUnicastSignatures())
                    {
                        try
                        {
                            var requestNodes = new List<Node>();

                            foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), otherNodes, 2))
                            {
                                requestNodes.Add(node);
                            }

                            for (int i = 0; i < requestNodes.Count; i++)
                            {
                                messageManagers[requestNodes[i]].PullUnicastMetadatasRequest.Add(signature);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    // Multicast
                    {
                        foreach (var tag in _settings.MetadataManager.GetMulticastTags())
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    messageManagers[requestNodes[i]].PullMulticastMetadatasRequest.Add(tag);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }
                    }
                }

                // Metadataのダウンロード
                if (connectionCount >= _downloadingConnectionCountLowerLimit
                    && pushMetadataDownloadStopwatch.Elapsed.TotalSeconds >= 60)
                {
                    pushMetadataDownloadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var pushBroadcastSignaturesRequestList = new List<string>();
                    var pushUnicastSignaturesRequestList = new List<string>();
                    var pushMulticastTagsRequestList = new List<Tag>();

                    // Broadcast
                    {
                        {
                            var array = _pushBroadcastMetadatasRequestList.ToArray();
                            _random.Shuffle(array);

                            int count = _maxMetadataRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                pushBroadcastSignaturesRequestList.Add(array[i]);

                                count--;
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullBroadcastMetadatasRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushBroadcastSignaturesRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }
                    }

                    // Unicast
                    {
                        {
                            var array = _pushUnicastMetadatasRequestList.ToArray();
                            _random.Shuffle(array);

                            int count = _maxMetadataRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                pushUnicastSignaturesRequestList.Add(array[i]);

                                count--;
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullUnicastMetadatasRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushUnicastSignaturesRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }
                    }

                    // Multicast
                    {
                        {
                            var array = _pushMulticastMetadatasRequestList.ToArray();
                            _random.Shuffle(array);

                            int count = _maxMetadataRequestCount;

                            for (int i = 0; count > 0 && i < array.Length; i++)
                            {
                                pushMulticastTagsRequestList.Add(array[i]);

                                count--;
                            }
                        }

                        foreach (var pair in messageManagers)
                        {
                            var node = pair.Key;
                            var messageManager = pair.Value;

                            {
                                var array = messageManager.PullMulticastMetadatasRequest.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushMulticastTagsRequestList.Add(array[i]);

                                    count--;
                                }
                            }
                        }
                    }

                    _random.Shuffle(pushBroadcastSignaturesRequestList);
                    _random.Shuffle(pushUnicastSignaturesRequestList);
                    _random.Shuffle(pushMulticastTagsRequestList);

                    // Broadcast
                    {
                        var pushBroadcastSignaturesRequestDictionary = new Dictionary<Node, HashSet<string>>();

                        foreach (var signature in pushBroadcastSignaturesRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<string> collection;

                                    if (!pushBroadcastSignaturesRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<string>();
                                        pushBroadcastSignaturesRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxMetadataRequestCount)
                                    {
                                        collection.Add(signature);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushBroadcastMetadatasRequestDictionary.ThisLock)
                        {
                            _pushBroadcastMetadatasRequestDictionary.Clear();

                            foreach (var pair in pushBroadcastSignaturesRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushBroadcastMetadatasRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                            }
                        }
                    }

                    // Unicast
                    {
                        var pushUnicastSignaturesRequestDictionary = new Dictionary<Node, HashSet<string>>();

                        foreach (var signature in pushUnicastSignaturesRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(Signature.GetHash(signature), otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<string> collection;

                                    if (!pushUnicastSignaturesRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<string>();
                                        pushUnicastSignaturesRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxMetadataRequestCount)
                                    {
                                        collection.Add(signature);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushUnicastMetadatasRequestDictionary.ThisLock)
                        {
                            _pushUnicastMetadatasRequestDictionary.Clear();

                            foreach (var pair in pushUnicastSignaturesRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushUnicastMetadatasRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                            }
                        }
                    }

                    // Multicast
                    {
                        var pushMulticastTagsRequestDictionary = new Dictionary<Node, HashSet<Tag>>();

                        foreach (var tag in pushMulticastTagsRequestList)
                        {
                            try
                            {
                                var requestNodes = new List<Node>();

                                foreach (var node in Kademlia<Node>.Search(tag.Id, otherNodes, 2))
                                {
                                    requestNodes.Add(node);
                                }

                                for (int i = 0; i < requestNodes.Count; i++)
                                {
                                    HashSet<Tag> collection;

                                    if (!pushMulticastTagsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                    {
                                        collection = new HashSet<Tag>();
                                        pushMulticastTagsRequestDictionary[requestNodes[i]] = collection;
                                    }

                                    if (collection.Count < _maxMetadataRequestCount)
                                    {
                                        collection.Add(tag);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }

                        lock (_pushMulticastMetadatasRequestDictionary.ThisLock)
                        {
                            _pushMulticastMetadatasRequestDictionary.Clear();

                            foreach (var pair in pushMulticastTagsRequestDictionary)
                            {
                                var node = pair.Key;
                                var targets = pair.Value;

                                _pushMulticastMetadatasRequestDictionary.Add(node, new List<Tag>(targets.Randomize()));
                            }
                        }
                    }
                }
            }
        }

        private void ConnectionManagerThread(object state)
        {
            Thread.CurrentThread.Name = "ConnectionsManager_ConnectionManagerThread";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            var connectionManager = state as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                var messageManager = _messagesManager[connectionManager.Node];

                var nodeUpdateTime = new Stopwatch();
                var updateTime = new Stopwatch();
                updateTime.Start();
                var blockDiffusionTime = new Stopwatch();
                blockDiffusionTime.Start();
                var metadataUpdateTime = new Stopwatch();
                metadataUpdateTime.Start();

                for (;;)
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_connectionManagers.Contains(connectionManager)) return;

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _connectionManagers.Count;
                    }

                    // PushNodes
                    if (!nodeUpdateTime.IsRunning || nodeUpdateTime.Elapsed.TotalMinutes >= 3)
                    {
                        nodeUpdateTime.Restart();

                        var nodes = new HashSet<Node>();

                        lock (this.ThisLock)
                        {
                            foreach (var node in _routeTable.Randomize())
                            {
                                if (nodes.Count >= 64) break;

                                if (node.Uris.Any(n => _succeededUris.Contains(n)))
                                {
                                    nodes.Add(node);
                                }
                            }

                            foreach (var node in _routeTable.Randomize())
                            {
                                if (nodes.Count >= 128) break;

                                nodes.Add(node);
                            }
                        }

                        if (nodes.Count > 0)
                        {
                            connectionManager.PushNodes(nodes.Randomize());

                            Debug.WriteLine(string.Format("ConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount.Add(nodes.Count);
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds >= 60)
                    {
                        updateTime.Restart();

                        // PushBlocksLink
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            List<Key> targetList = null;

                            lock (_pushBlocksLinkDictionary.ThisLock)
                            {
                                if (_pushBlocksLinkDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBlocksLinkDictionary.Remove(connectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                connectionManager.PushBlocksLink(targetList);

                                Debug.WriteLine(string.Format("ConnectionManager: Push BlocksLink ({0})", targetList.Count));
                                _pushBlockLinkCount.Add(targetList.Count);
                            }
                        }

                        // PushBlocksRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<Key> targetList = null;

                            lock (_pushBlocksRequestDictionary.ThisLock)
                            {
                                if (_pushBlocksRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBlocksRequestDictionary.Remove(connectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                connectionManager.PushBlocksRequest(targetList);

                                Debug.WriteLine(string.Format("ConnectionManager: Push BlocksRequest ({0})", targetList.Count));
                                _pushBlockRequestCount.Add(targetList.Count);
                            }
                        }

                        // PushBroadcastMetadatasRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<string> targetList = null;

                            lock (_pushBroadcastMetadatasRequestDictionary.ThisLock)
                            {
                                if (_pushBroadcastMetadatasRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushBroadcastMetadatasRequestDictionary.Remove(connectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                connectionManager.PushBroadcastMetadatasRequest(targetList);

                                foreach (var item in targetList)
                                {
                                    _pushBroadcastMetadatasRequestList.Remove(item);
                                }

                                Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastMetadatasRequest ({0})", targetList.Count));
                                _pushMetadataRequestCount.Add(targetList.Count);
                            }
                        }

                        // PushUnicastMetadatasRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<string> targetList = null;

                            lock (_pushUnicastMetadatasRequestDictionary.ThisLock)
                            {
                                if (_pushUnicastMetadatasRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushUnicastMetadatasRequestDictionary.Remove(connectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                connectionManager.PushUnicastMetadatasRequest(targetList);

                                foreach (var item in targetList)
                                {
                                    _pushUnicastMetadatasRequestList.Remove(item);
                                }

                                Debug.WriteLine(string.Format("ConnectionManager: Push UnicastMetadatasRequest ({0})", targetList.Count));
                                _pushMetadataRequestCount.Add(targetList.Count);
                            }
                        }

                        // PushMulticastMetadatasRequest
                        if (connectionCount >= _downloadingConnectionCountLowerLimit)
                        {
                            List<Tag> targetList = null;

                            lock (_pushMulticastMetadatasRequestDictionary.ThisLock)
                            {
                                if (_pushMulticastMetadatasRequestDictionary.TryGetValue(connectionManager.Node, out targetList))
                                {
                                    _pushMulticastMetadatasRequestDictionary.Remove(connectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                connectionManager.PushMulticastMetadatasRequest(targetList);

                                foreach (var item in targetList)
                                {
                                    _pushMulticastMetadatasRequestList.Remove(item);
                                }

                                Debug.WriteLine(string.Format("ConnectionManager: Push MulticastMetadatasRequest ({0})", targetList.Count));
                                _pushMetadataRequestCount.Add(targetList.Count);
                            }
                        }
                    }

                    if (blockDiffusionTime.Elapsed.TotalSeconds >= 5)
                    {
                        blockDiffusionTime.Restart();

                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_diffusionBlocksDictionary.ThisLock)
                            {
                                Queue<Key> queue;

                                if (_diffusionBlocksDictionary.TryGetValue(connectionManager.Node, out queue))
                                {
                                    if (queue.Count > 0)
                                    {
                                        key = queue.Dequeue();
                                        messageManager.StockBlocks.Add(key);
                                    }
                                }
                            }

                            if (key != null)
                            {
                                var buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block (Diffusion) ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
                                    _pushBlockCount.Increment();

                                    messageManager.PullBlocksRequest.Remove(key);
                                }
                                catch (ConnectionManagerException e)
                                {
                                    messageManager.StockBlocks.Remove(key);

                                    throw e;
                                }
                                catch (BlockNotFoundException)
                                {
                                    messageManager.StockBlocks.Remove(key);
                                }
                                finally
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }
                                }

                                _settings.UploadBlocksRequest.Remove(key);
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }
                    }

                    if (_random.NextDouble() < this.GetPriority(connectionManager.Node))
                    {
                        // PushBlock
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            Key key = null;

                            lock (_uploadBlocksDictionary.ThisLock)
                            {
                                Queue<Key> queue;

                                if (_uploadBlocksDictionary.TryGetValue(connectionManager.Node, out queue))
                                {
                                    if (queue.Count > 0)
                                    {
                                        key = queue.Dequeue();
                                        messageManager.StockBlocks.Add(key);
                                    }
                                }
                            }

                            if (key != null)
                            {
                                var buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = _cacheManager[key];

                                    connectionManager.PushBlock(key, buffer);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push Block (Upload) ({0})", NetworkConverter.ToBase64UrlString(key.Hash)));
                                    _pushBlockCount.Increment();

                                    messageManager.PullBlocksRequest.Remove(key);

                                    messageManager.Priority.Decrement();

                                    // Infomation
                                    {
                                        if (_relayBlocks.Contains(key))
                                        {
                                            _relayBlockCount.Increment();
                                        }
                                    }
                                }
                                catch (ConnectionManagerException e)
                                {
                                    messageManager.StockBlocks.Remove(key);

                                    throw e;
                                }
                                catch (BlockNotFoundException)
                                {
                                    messageManager.StockBlocks.Remove(key);
                                }
                                finally
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }
                                }

                                _settings.UploadBlocksRequest.Remove(key);
                                _settings.DiffusionBlocksRequest.Remove(key);
                            }
                        }
                    }

                    if (metadataUpdateTime.Elapsed.TotalSeconds >= 60)
                    {
                        metadataUpdateTime.Restart();

                        // PushBroadcastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullBroadcastMetadatasRequest.ToArray();

                                var broadcastMetadats = new List<BroadcastMetadata>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    var metadata = _settings.MetadataManager.GetBroadcastMetadata(signature);
                                    if (metadata == null) continue;

                                    if (!messageManager.StockBroadcastMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                    {
                                        broadcastMetadats.Add(metadata);

                                        if (broadcastMetadats.Count >= _maxMetadataCount) break;
                                    }

                                    if (broadcastMetadats.Count >= _maxMetadataCount) break;
                                }

                                if (broadcastMetadats.Count > 0)
                                {
                                    connectionManager.PushBroadcastMetadatas(broadcastMetadats);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push BroadcastMetadatas ({0})", broadcastMetadats.Count));
                                    _pushMetadataCount.Add(broadcastMetadats.Count);

                                    foreach (var metadata in broadcastMetadats)
                                    {
                                        messageManager.StockBroadcastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushUnicastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var signatures = messageManager.PullUnicastMetadatasRequest.ToArray();

                                var unicastMetadata = new List<UnicastMetadata>();

                                _random.Shuffle(signatures);
                                foreach (var signature in signatures)
                                {
                                    foreach (var metadata in _settings.MetadataManager.GetUnicastMetadatas(signature))
                                    {
                                        if (!messageManager.StockUnicastMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            unicastMetadata.Add(metadata);

                                            if (unicastMetadata.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    if (unicastMetadata.Count >= _maxMetadataCount) break;
                                }

                                if (unicastMetadata.Count > 0)
                                {
                                    connectionManager.PushUnicastMetadatas(unicastMetadata);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push UnicastMetadatas ({0})", unicastMetadata.Count));
                                    _pushMetadataCount.Add(unicastMetadata.Count);

                                    foreach (var metadata in unicastMetadata)
                                    {
                                        messageManager.StockUnicastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }

                        // PushMulticastMetadatas
                        if (connectionCount >= _uploadingConnectionCountLowerLimit)
                        {
                            {
                                var tags = messageManager.PullMulticastMetadatasRequest.ToArray();

                                var multicastMetadatas = new List<MulticastMetadata>();

                                _random.Shuffle(tags);
                                foreach (var tag in tags)
                                {
                                    foreach (var metadata in _settings.MetadataManager.GetMulticastMetadatas(tag))
                                    {
                                        if (!messageManager.StockMulticastMetadatas.Contains(metadata.CreateHash(_hashAlgorithm)))
                                        {
                                            multicastMetadatas.Add(metadata);

                                            if (multicastMetadatas.Count >= _maxMetadataCount) break;
                                        }
                                    }

                                    if (multicastMetadatas.Count >= _maxMetadataCount) break;
                                }

                                if (multicastMetadatas.Count > 0)
                                {
                                    connectionManager.PushMulticastMetadatas(multicastMetadatas);

                                    Debug.WriteLine(string.Format("ConnectionManager: Push MulticastMetadatas ({0})", multicastMetadatas.Count));
                                    _pushMetadataCount.Add(multicastMetadatas.Count);

                                    foreach (var metadata in multicastMetadatas)
                                    {
                                        messageManager.StockMulticastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
            finally
            {
                this.RemoveConnectionManager(connectionManager);
            }
        }

        #region connectionManager_Event

        private void connectionManager_NodesEvent(object sender, PullNodesEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            foreach (var node in e.Nodes.Take(_maxNodeCount))
            {
                if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount.Increment();
            }
        }

        private void connectionManager_BlocksLinkEvent(object sender, PullBlocksLinkEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBlocksLink.Count > _maxBlockLinkCount * messageManager.PullBlocksLink.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksLink ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockLinkCount))
            {
                if (!ConnectionsManager.Check(key)) continue;

                messageManager.PullBlocksLink.Add(key);
                _pullBlockLinkCount.Increment();
            }
        }

        private void connectionManager_BlocksRequestEvent(object sender, PullBlocksRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBlocksRequest.Count > _maxBlockRequestCount * messageManager.PullBlocksRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BlocksRequest ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxBlockRequestCount))
            {
                if (!ConnectionsManager.Check(key)) continue;

                messageManager.PullBlocksRequest.Add(key);
                _pullBlockRequestCount.Increment();
            }
        }

        private void connectionManager_BlockEvent(object sender, PullBlockEventArgs e)
        {
            // tryですべて囲まないとメモリーリークの恐れあり。
            try
            {
                var connectionManager = sender as ConnectionManager;
                if (connectionManager == null) return;

                var messageManagers = new Dictionary<Node, MessageManager>();
                {
                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_connectionManagers.Select(n => n.Node));
                    }

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }
                }

                var messageManager = messageManagers[connectionManager.Node];

                if (!ConnectionsManager.Check(e.Key) || e.Value.Array == null) return;

                _cacheManager[e.Key] = e.Value;

                if (_downloadBlocks.Contains(e.Key) || messageManagers.Values.Any(n => n.PullBlocksRequest.Contains(e.Key)))
                {
                    Debug.WriteLine(string.Format("ConnectionManager: Pull Block (Upload) ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));

                    messageManager.LastPullTime = DateTime.UtcNow;
                    messageManager.Priority.Increment();

                    // Information
                    {
                        _relayBlocks.Add(e.Key);
                    }
                }
                else
                {
                    Debug.WriteLine(string.Format("ConnectionManager: Pull Block (Diffusion) ({0})", NetworkConverter.ToBase64UrlString(e.Key.Hash)));

                    _settings.DiffusionBlocksRequest.Add(e.Key);
                }

                messageManager.StockBlocks.Add(e.Key);
                _pullBlockCount.Increment();
            }
            finally
            {
                if (e.Value.Array != null)
                {
                    _bufferManager.ReturnBuffer(e.Value.Array);
                }
            }
        }

        private void connectionManager_PullBroadcastMetadatasRequestEvent(object sender, PullBroadcastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullBroadcastMetadatasRequest.Count > _maxMetadataRequestCount * messageManager.PullBroadcastMetadatasRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastMetadatasRequest ({0})", e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxMetadataRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullBroadcastMetadatasRequest.Add(signature);
                _pullMetadataRequestCount.Increment();

                _broadcastMetadataLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullBroadcastMetadatasEvent(object sender, PullBroadcastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockBroadcastMetadatas.Count > _maxMetadataCount * messageManager.StockBroadcastMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull BroadcastMetadatas ({0})", e.BroadcastMetadatas.Count()));

            foreach (var metadata in e.BroadcastMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockBroadcastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    var signature = metadata.Certificate.ToString();

                    _broadcastMetadataLastAccessTimes[signature] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }
        }

        private void connectionManager_PullUnicastMetadatasRequestEvent(object sender, PullUnicastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullUnicastMetadatasRequest.Count > _maxMetadataRequestCount * messageManager.PullUnicastMetadatasRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastMetadatasRequest ({0})", e.Signatures.Count()));

            foreach (var signature in e.Signatures.Take(_maxMetadataRequestCount))
            {
                if (!Signature.Check(signature)) continue;

                messageManager.PullUnicastMetadatasRequest.Add(signature);
                _pullMetadataRequestCount.Increment();

                _unicastMetadataLastAccessTimes[signature] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullUnicastMetadatasEvent(object sender, PullUnicastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockUnicastMetadatas.Count > _maxMetadataCount * messageManager.StockUnicastMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull UnicastMetadatas ({0})", e.UnicastMetadatas.Count()));

            foreach (var metadata in e.UnicastMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockUnicastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _unicastMetadataLastAccessTimes[metadata.Signature] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }
        }

        private void connectionManager_PullMulticastMetadatasRequestEvent(object sender, PullMulticastMetadatasRequestEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.PullMulticastMetadatasRequest.Count > _maxMetadataRequestCount * messageManager.PullMulticastMetadatasRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastMetadatasRequest ({0})", e.Tags.Count()));

            foreach (var tag in e.Tags.Take(_maxMetadataRequestCount))
            {
                if (!ConnectionsManager.Check(tag)) continue;

                messageManager.PullMulticastMetadatasRequest.Add(tag);
                _pullMetadataRequestCount.Increment();

                _multicastMetadataLastAccessTimes[tag] = DateTime.UtcNow;
            }
        }

        private void connectionManager_PullMulticastMetadatasEvent(object sender, PullMulticastMetadatasEventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            var messageManager = _messagesManager[connectionManager.Node];

            if (messageManager.StockMulticastMetadatas.Count > _maxMetadataCount * messageManager.StockMulticastMetadatas.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("ConnectionManager: Pull MulticastMetadatas ({0})", e.MulticastMetadatas.Count()));

            foreach (var metadata in e.MulticastMetadatas.Take(_maxMetadataCount))
            {
                if (_settings.MetadataManager.SetMetadata(metadata))
                {
                    messageManager.StockMulticastMetadatas.Add(metadata.CreateHash(_hashAlgorithm));

                    _multicastMetadataLastAccessTimes[metadata.Tag] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }
        }

        private void connectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            Debug.WriteLine("ConnectionManager: Pull Cancel");

            try
            {
                lock (this.ThisLock)
                {
                    this.RemoveNode(connectionManager.Node);
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        private void connectionManager_CloseEvent(object sender, EventArgs e)
        {
            var connectionManager = sender as ConnectionManager;
            if (connectionManager == null) return;

            try
            {
                lock (this.ThisLock)
                {
                    if (!_removeNodes.Contains(connectionManager.Node))
                    {
                        _cuttingNodes.Add(connectionManager.Node);
                    }
                }

                this.RemoveConnectionManager(connectionManager);
            }
            catch (Exception)
            {

            }
        }

        #endregion

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!ConnectionsManager.Check(baseNode)) throw new ArgumentException("baseNode");

            lock (this.ThisLock)
            {
                _routeTable.BaseNode = baseNode;
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                foreach (var node in nodes)
                {
                    if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
                }
            }
        }

        public bool IsDownloadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_downloadBlocks.Contains(key))
                    return true;

                return false;
            }
        }

        public bool IsUploadWaiting(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_settings.UploadBlocksRequest.Contains(key))
                    return true;

                return false;
            }
        }

        public void Download(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _downloadBlocks.Add(key);
            }
        }

        public void Upload(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.UploadBlocksRequest.Add(key);
            }
        }

        public BroadcastMetadata GetBroadcastMetadata(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushBroadcastMetadatasRequestList.Add(signature);

                return _settings.MetadataManager.GetBroadcastMetadata(signature);
            }
        }

        public IEnumerable<UnicastMetadata> GetUnicastMetadatas(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushUnicastMetadatasRequestList.Add(signature);

                return _settings.MetadataManager.GetUnicastMetadatas(signature);
            }
        }

        public IEnumerable<MulticastMetadata> GetMulticastMetadatas(Tag tag)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushMulticastMetadatasRequestList.Add(tag);

                return _settings.MetadataManager.GetMulticastMetadatas(tag);
            }
        }

        public void Upload(BroadcastMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(UnicastMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public void Upload(MulticastMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.MetadataManager.SetMetadata(metadata);
            }
        }

        public override ManagerState State
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

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

                    this.UpdateSessionId();

                    _serverManager.Start();

                    _connectionsManagerThread = new Thread(this.ConnectionsManagerThread);
                    _connectionsManagerThread.Name = "ConnectionsManager_ConnectionsManagerThread";
                    _connectionsManagerThread.Priority = ThreadPriority.Lowest;
                    _connectionsManagerThread.Start();

                    for (int i = 0; i < 3; i++)
                    {
                        var thread = new Thread(this.CreateConnectionThread);
                        thread.Name = "ConnectionsManager_CreateConnectionThread";
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Start();

                        _createConnectionThreads.Add(thread);
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        var thread = new Thread(this.AcceptConnectionThread);
                        thread.Name = "ConnectionsManager_AcceptConnectionThread";
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Start();

                        _acceptConnectionThreads.Add(thread);
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

                    _serverManager.Stop();
                }

                foreach (var thread in _createConnectionThreads)
                {
                    thread.Join();
                }
                _createConnectionThreads.Clear();

                foreach (var thread in _acceptConnectionThreads)
                {
                    thread.Join();
                }
                _acceptConnectionThreads.Clear();

                _connectionsManagerThread.Join();
                _connectionsManagerThread = null;

                lock (this.ThisLock)
                {
                    foreach (var item in _connectionManagers.ToArray())
                    {
                        this.RemoveConnectionManager(item);
                    }

                    _cuttingNodes.Clear();
                    _removeNodes.Clear();

                    _messagesManager.Clear();
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _routeTable.BaseNode = _settings.BaseNode;

                foreach (var node in _settings.OtherNodes.ToArray())
                {
                    if (!ConnectionsManager.Check(node) || node.Uris.Count() == 0) continue;

                    _routeTable.Add(node);
                }

                _bandwidthLimit.In = _settings.BandwidthLimit;
                _bandwidthLimit.Out = _settings.BandwidthLimit;
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.BaseNode = _routeTable.BaseNode;

                {
                    var otherNodes = _routeTable.ToArray();

                    lock (_settings.OtherNodes.ThisLock)
                    {
                        _settings.OtherNodes.Clear();
                        _settings.OtherNodes.AddRange(otherNodes);
                    }
                }

                _settings.BandwidthLimit = (_bandwidthLimit.In + _bandwidthLimit.Out) / 2;

                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            private MetadataManager _metadataManager = new MetadataManager();

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 32 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "DiffusionBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Key>>() { Name = "UploadBlocksRequest", Value = new LockedHashSet<Key>() },
                    new Library.Configuration.SettingContent<List<BroadcastMetadata>>() { Name = "BroadcastMetadatas", Value = new List<BroadcastMetadata>() },
                    new Library.Configuration.SettingContent<List<UnicastMetadata>>() { Name = "UnicastMetadatas", Value = new List<UnicastMetadata>() },
                    new Library.Configuration.SettingContent<List<MulticastMetadata>>() { Name = "MulticastMetadatas", Value = new List<MulticastMetadata>() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);

                    foreach (var metadata in this.BroadcastMetadatas)
                    {
                        try
                        {
                            _metadataManager.SetMetadata(metadata);
                        }
                        catch (Exception)
                        {

                        }
                    }

                    foreach (var metadata in this.UnicastMetadatas)
                    {
                        try
                        {
                            _metadataManager.SetMetadata(metadata);
                        }
                        catch (Exception)
                        {

                        }
                    }

                    foreach (var metadata in this.MulticastMetadatas)
                    {
                        try
                        {
                            _metadataManager.SetMetadata(metadata);
                        }
                        catch (Exception)
                        {

                        }
                    }

                    this.BroadcastMetadatas.Clear();
                    this.UnicastMetadatas.Clear();
                    this.MulticastMetadatas.Clear();
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    this.BroadcastMetadatas.AddRange(_metadataManager.GetBroadcastMetadatas());
                    this.UnicastMetadatas.AddRange(_metadataManager.GetUnicastMetadatas());
                    this.MulticastMetadatas.AddRange(_metadataManager.GetMulticastMetadatas());

                    base.Save(directoryPath);

                    this.BroadcastMetadatas.Clear();
                    this.UnicastMetadatas.Clear();
                    this.MulticastMetadatas.Clear();
                }
            }

            public Node BaseNode
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Node)this["BaseNode"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["BaseNode"] = value;
                    }
                }
            }

            public NodeCollection OtherNodes
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (NodeCollection)this["OtherNodes"];
                    }
                }
            }

            public int ConnectionCountLimit
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (int)this["ConnectionCountLimit"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["ConnectionCountLimit"] = value;
                    }
                }
            }

            public int BandwidthLimit
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (int)this["BandwidthLimit"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["BandwidthLimit"] = value;
                    }
                }
            }

            public LockedHashSet<Key> DiffusionBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Key>)this["DiffusionBlocksRequest"];
                    }
                }
            }

            public LockedHashSet<Key> UploadBlocksRequest
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Key>)this["UploadBlocksRequest"];
                    }
                }
            }

            public MetadataManager MetadataManager
            {
                get
                {
                    lock (_thisLock)
                    {
                        return _metadataManager;
                    }
                }
            }

            private List<BroadcastMetadata> BroadcastMetadatas
            {
                get
                {
                    return (List<BroadcastMetadata>)this["BroadcastMetadatas"];
                }
            }

            private List<UnicastMetadata> UnicastMetadatas
            {
                get
                {
                    return (List<UnicastMetadata>)this["UnicastMetadatas"];
                }
            }

            private List<MulticastMetadata> MulticastMetadatas
            {
                get
                {
                    return (List<MulticastMetadata>)this["MulticastMetadatas"];
                }
            }
        }

        public class MetadataManager
        {
            private Dictionary<string, BroadcastMetadata> _broadcastMetadats = new Dictionary<string, BroadcastMetadata>();
            private Dictionary<string, HashSet<UnicastMetadata>> _unicastMetadata = new Dictionary<string, HashSet<UnicastMetadata>>();
            private Dictionary<Tag, Dictionary<string, HashSet<MulticastMetadata>>> _multicastMetadatas = new Dictionary<Tag, Dictionary<string, HashSet<MulticastMetadata>>>();

            private readonly object _thisLock = new object();

            public MetadataManager()
            {

            }

            public int Count
            {
                get
                {
                    lock (_thisLock)
                    {
                        int count = 0;

                        count += _broadcastMetadats.Count;
                        count += _unicastMetadata.Values.Sum(n => n.Count);
                        count += _multicastMetadatas.Values.Sum(n => n.Values.Sum(m => m.Count));

                        return count;
                    }
                }
            }

            public IEnumerable<string> GetBroadcastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_broadcastMetadats.Keys);

                    return hashset;
                }
            }

            public IEnumerable<string> GetUnicastSignatures()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<string>();

                    hashset.UnionWith(_unicastMetadata.Keys);

                    return hashset;
                }
            }

            public IEnumerable<Tag> GetMulticastTags()
            {
                lock (_thisLock)
                {
                    var hashset = new HashSet<Tag>();

                    hashset.UnionWith(_multicastMetadatas.Keys);

                    return hashset;
                }
            }

            public void RemoveBroadcastSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _broadcastMetadats.Remove(signature);
                    }
                }
            }

            public void RemoveUnicastSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        _unicastMetadata.Remove(signature);
                    }
                }
            }

            public void RemoveMulticastTags(IEnumerable<Tag> tags)
            {
                lock (_thisLock)
                {
                    foreach (var tag in tags)
                    {
                        _multicastMetadatas.Remove(tag);
                    }
                }
            }

            public IEnumerable<BroadcastMetadata> GetBroadcastMetadatas()
            {
                lock (_thisLock)
                {
                    return _broadcastMetadats.Values.ToArray();
                }
            }

            public BroadcastMetadata GetBroadcastMetadata(string signature)
            {
                lock (_thisLock)
                {
                    BroadcastMetadata metadata;

                    if (_broadcastMetadats.TryGetValue(signature, out metadata))
                    {
                        return metadata;
                    }

                    return null;
                }
            }

            public IEnumerable<UnicastMetadata> GetUnicastMetadatas()
            {
                lock (_thisLock)
                {
                    return _unicastMetadata.Values.Extract().ToArray();
                }
            }

            public IEnumerable<UnicastMetadata> GetUnicastMetadatas(string signature)
            {
                lock (_thisLock)
                {
                    HashSet<UnicastMetadata> hashset;

                    if (_unicastMetadata.TryGetValue(signature, out hashset))
                    {
                        return hashset.ToArray();
                    }

                    return new UnicastMetadata[0];
                }
            }

            public IEnumerable<MulticastMetadata> GetMulticastMetadatas()
            {
                lock (_thisLock)
                {
                    return _multicastMetadatas.Values.SelectMany(n => n.Values.Extract()).ToArray();
                }
            }

            public IEnumerable<MulticastMetadata> GetMulticastMetadatas(Tag tag)
            {
                lock (_thisLock)
                {
                    Dictionary<string, HashSet<MulticastMetadata>> dic = null;

                    if (_multicastMetadatas.TryGetValue(tag, out dic))
                    {
                        return dic.Values.Extract().ToArray();
                    }

                    return new MulticastMetadata[0];
                }
            }

            public bool SetMetadata(BroadcastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    BroadcastMetadata tempMetadata;

                    if (!_broadcastMetadats.TryGetValue(signature, out tempMetadata)
                        || metadata.CreationTime > tempMetadata.CreationTime)
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        _broadcastMetadats[signature] = metadata;
                    }

                    return (tempMetadata == null || metadata.CreationTime >= tempMetadata.CreationTime);
                }
            }

            public bool SetMetadata(UnicastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || !Signature.Check(metadata.Signature)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    HashSet<UnicastMetadata> hashset;

                    if (!_unicastMetadata.TryGetValue(metadata.Signature, out hashset))
                    {
                        hashset = new HashSet<UnicastMetadata>();
                        _unicastMetadata[metadata.Signature] = hashset;
                    }

                    if (!hashset.Contains(metadata))
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(metadata);
                    }

                    return true;
                }
            }

            public bool SetMetadata(MulticastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return false;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<MulticastMetadata>> dic;

                    if (!_multicastMetadatas.TryGetValue(metadata.Tag, out dic))
                    {
                        dic = new Dictionary<string, HashSet<MulticastMetadata>>();
                        _multicastMetadatas[metadata.Tag] = dic;
                    }

                    HashSet<MulticastMetadata> hashset;

                    if (!dic.TryGetValue(signature, out hashset))
                    {
                        hashset = new HashSet<MulticastMetadata>();
                        dic[signature] = hashset;
                    }

                    if (!hashset.Contains(metadata))
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        hashset.Add(metadata);
                    }

                    return true;
                }
            }

            public void RemoveMetadata(BroadcastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    _broadcastMetadats.Remove(signature);
                }
            }

            public void RemoveMetadata(UnicastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || !Signature.Check(metadata.Signature)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    HashSet<UnicastMetadata> hashset;
                    if (!_unicastMetadata.TryGetValue(metadata.Signature, out hashset)) return;

                    hashset.Remove(metadata);

                    if (hashset.Count == 0)
                    {
                        _unicastMetadata.Remove(metadata.Signature);
                    }
                }
            }

            public void RemoveMetadata(MulticastMetadata metadata)
            {
                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    if (metadata == null
                        || metadata.Tag == null
                            || metadata.Tag.Id == null || metadata.Tag.Id.Length == 0
                            || string.IsNullOrWhiteSpace(metadata.Tag.Name)
                        || (metadata.CreationTime - now).Minutes > 30
                        || metadata.Certificate == null) return;

                    var signature = metadata.Certificate.ToString();

                    Dictionary<string, HashSet<MulticastMetadata>> dic;
                    if (!_multicastMetadatas.TryGetValue(metadata.Tag, out dic)) return;

                    HashSet<MulticastMetadata> hashset;
                    if (!dic.TryGetValue(signature, out hashset)) return;

                    hashset.Remove(metadata);

                    if (hashset.Count == 0)
                    {
                        dic.Remove(signature);

                        if (dic.Count == 0)
                        {
                            _multicastMetadatas.Remove(metadata.Tag);
                        }
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
                if (_refreshTimer != null)
                {
                    try
                    {
                        _refreshTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _refreshTimer = null;
                }

                if (_messagesManager != null)
                {
                    try
                    {
                        _messagesManager.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _messagesManager = null;
                }

                if (_bandwidthLimit != null)
                {
                    try
                    {
                        _bandwidthLimit.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _bandwidthLimit = null;
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
    class ConnectionsManagerException : ManagerException
    {
        public ConnectionsManagerException() : base() { }
        public ConnectionsManagerException(string message) : base(message) { }
        public ConnectionsManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class CertificateException : ConnectionsManagerException
    {
        public CertificateException() : base() { }
        public CertificateException(string message) : base(message) { }
        public CertificateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
