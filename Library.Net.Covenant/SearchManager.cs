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

namespace Library.Net.Covenant
{
    public delegate IEnumerable<string> GetSignaturesEventHandler(object sender);

    public delegate Cap CreateCapEventHandler(object sender, string uri);
    public delegate Cap AcceptCapEventHandler(object sender, out string uri);

    class SearchManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private Settings _settings;

        private Random _random = new Random();

        private Kademlia<Node> _routeTable;

        private byte[] _mySessionId;

        private LockedList<SearchConnectionManager> _searchConnectionManagers;
        private MessagesManager _messagesManager;
        private LocationsManager _locationsManager;

        private LockedHashDictionary<Node, List<Key>> _pushLocationsRequestDictionary = new LockedHashDictionary<Node, List<Key>>();
        private LockedHashDictionary<Node, List<string>> _pushLinkMetadatasRequestDictionary = new LockedHashDictionary<Node, List<string>>();
        private LockedHashDictionary<Node, List<string>> _pushStoreMetadatasRequestDictionary = new LockedHashDictionary<Node, List<string>>();

        private WatchTimer _refreshTimer;

        private LockedList<Node> _creatingNodes;

        private VolatileHashSet<Node> _waitingNodes;
        private VolatileHashSet<Node> _cuttingNodes;
        private VolatileHashSet<Node> _removeNodes;

        private VolatileHashSet<string> _succeededUris;

        private VolatileHashSet<Key> _pushLocationsRequestList;
        private VolatileHashSet<string> _pushLinkMetadatasRequestList;
        private VolatileHashSet<string> _pushStoreMetadatasRequestList;

        private LockedHashDictionary<string, DateTime> _linkMetadataLastAccessTimes = new LockedHashDictionary<string, DateTime>();
        private LockedHashDictionary<string, DateTime> _storeMetadataLastAccessTimes = new LockedHashDictionary<string, DateTime>();

        private Thread _searchManagerThread;
        private List<Thread> _createConnectionThreads = new List<Thread>();
        private List<Thread> _acceptConnectionThreads = new List<Thread>();

        private volatile ManagerState _state = ManagerState.Stop;

        private Dictionary<Node, string> _nodeToUri = new Dictionary<Node, string>();

        private BandwidthLimit _bandwidthLimit = new BandwidthLimit();

        private long _receivedByteCount;
        private long _sentByteCount;

        private readonly SafeInteger _pushNodeCount = new SafeInteger();
        private readonly SafeInteger _pushLocationRequestCount = new SafeInteger();
        private readonly SafeInteger _pushLocationCount = new SafeInteger();
        private readonly SafeInteger _pushMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pushMetadataCount = new SafeInteger();

        private readonly SafeInteger _pullNodeCount = new SafeInteger();
        private readonly SafeInteger _pullLocationRequestCount = new SafeInteger();
        private readonly SafeInteger _pullLocationCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataRequestCount = new SafeInteger();
        private readonly SafeInteger _pullMetadataCount = new SafeInteger();

        private readonly SafeInteger _connectConnectionCount = new SafeInteger();
        private readonly SafeInteger _acceptConnectionCount = new SafeInteger();

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;

        private GetSignaturesEventHandler _getLockSignaturesEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxReceiveCount = 1024 * 1024;

        private const int _maxNodeCount = 128;
        private const int _maxLocationRequestCount = 1024;
        private const int _maxLocationCount = 8192;
        private const int _maxMetadataRequestCount = 1024;
        private const int _maxMetadataCount = 8192;

        private const int _routeTableMinCount = 100;

        public SearchManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _routeTable = new Kademlia<Node>(512, 20);

            _searchConnectionManagers = new LockedList<SearchConnectionManager>();

            _messagesManager = new MessagesManager();
            _messagesManager.GetLockNodesEvent = (object sender) =>
            {
                lock (this.ThisLock)
                {
                    return _searchConnectionManagers.Select(n => n.Node).ToArray();
                }
            };

            _locationsManager = new LocationsManager();

            _creatingNodes = new LockedList<Node>();

            _waitingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 0, 30));
            _cuttingNodes = new VolatileHashSet<Node>(new TimeSpan(0, 10, 0));
            _removeNodes = new VolatileHashSet<Node>(new TimeSpan(0, 30, 0));

            _succeededUris = new VolatileHashSet<string>(new TimeSpan(1, 0, 0));

            _pushLocationsRequestList = new VolatileHashSet<Key>(new TimeSpan(0, 3, 0));
            _pushLinkMetadatasRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));
            _pushStoreMetadatasRequestList = new VolatileHashSet<string>(new TimeSpan(0, 3, 0));

            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 5));
        }

        private void RefreshTimer()
        {
            _locationsManager.Refresh();

            _waitingNodes.TrimExcess();
            _cuttingNodes.TrimExcess();
            _removeNodes.TrimExcess();

            _succeededUris.TrimExcess();

            _pushLocationsRequestList.TrimExcess();
            _pushLinkMetadatasRequestList.TrimExcess();
            _pushStoreMetadatasRequestList.TrimExcess();
        }

        public CreateCapEventHandler CreateCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _createCapEvent = value;
                }
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

                    foreach (var searchConnectionManager in _searchConnectionManagers.ToArray())
                    {
                        var contexts = new List<InformationContext>();

                        var messageManager = _messagesManager[searchConnectionManager.Node];

                        contexts.Add(new InformationContext("Id", messageManager.Id));
                        contexts.Add(new InformationContext("Node", searchConnectionManager.Node));
                        contexts.Add(new InformationContext("Uri", _nodeToUri[searchConnectionManager.Node]));
                        contexts.Add(new InformationContext("ReceivedByteCount", (long)messageManager.ReceivedByteCount + searchConnectionManager.ReceivedByteCount));
                        contexts.Add(new InformationContext("SentByteCount", (long)messageManager.SentByteCount + searchConnectionManager.SentByteCount));
                        contexts.Add(new InformationContext("Direction", searchConnectionManager.Direction));

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
                    contexts.Add(new InformationContext("PushLocationRequestCount", (long)_pushLocationRequestCount));
                    contexts.Add(new InformationContext("PushLocationCount", (long)_pushLocationCount));
                    contexts.Add(new InformationContext("PushMetadataRequestCount", (long)_pushMetadataRequestCount));
                    contexts.Add(new InformationContext("PushMetadataCount", (long)_pushMetadataCount));

                    contexts.Add(new InformationContext("PullNodeCount", (long)_pullNodeCount));
                    contexts.Add(new InformationContext("PullLocationRequestCount", (long)_pullLocationRequestCount));
                    contexts.Add(new InformationContext("PullLocationCount", (long)_pullLocationCount));
                    contexts.Add(new InformationContext("PullMetadataRequestCount", (long)_pullMetadataRequestCount));
                    contexts.Add(new InformationContext("PullMetadataCount", (long)_pullMetadataCount));

                    contexts.Add(new InformationContext("CreateConnectionCount", (long)_connectConnectionCount));
                    contexts.Add(new InformationContext("AcceptConnectionCount", (long)_acceptConnectionCount));

                    contexts.Add(new InformationContext("OtherNodeCount", _routeTable.Count));

                    {
                        var nodes = new HashSet<Node>();

                        foreach (var searchConnectionManager in _searchConnectionManagers)
                        {
                            nodes.Add(searchConnectionManager.Node);
                        }

                        contexts.Add(new InformationContext("SurroundingNodeCount", nodes.Count));
                    }

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
                    return _receivedByteCount + _searchConnectionManagers.Sum(n => n.ReceivedByteCount);
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
                    return _sentByteCount + _searchConnectionManagers.Sum(n => n.SentByteCount);
                }
            }
        }

        protected virtual Cap OnCreateCapEvent(string uri)
        {
            return _createCapEvent?.Invoke(this, uri);
        }

        protected virtual Cap OnAcceptCapEvent(out string uri)
        {
            uri = null;
            return _acceptCapEvent?.Invoke(this, out uri);
        }

        protected virtual IEnumerable<string> OnLockSignaturesEvent()
        {
            return _getLockSignaturesEvent?.Invoke(this);
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

        private static bool Check(string signature)
        {
            return !(signature == null || !Signature.Check(signature));
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

        private void AddConnectionManager(SearchConnectionManager searchConnectionManager, string uri)
        {
            lock (this.ThisLock)
            {
                if (CollectionUtilities.Equals(searchConnectionManager.Node.Id, this.BaseNode.Id)
                    || _searchConnectionManagers.Any(n => CollectionUtilities.Equals(n.Node.Id, searchConnectionManager.Node.Id)))
                {
                    searchConnectionManager.Dispose();
                    return;
                }

                if (_searchConnectionManagers.Count >= this.ConnectionCountLimit)
                {
                    searchConnectionManager.Dispose();
                    return;
                }

                Debug.WriteLine("SearchConnectionManager: Connect");

                searchConnectionManager.PullNodesEvent += this.searchConnectionManager_NodesEvent;
                searchConnectionManager.PullLocationsRequestEvent += this.searchConnectionManager_LocationsRequestEvent;
                searchConnectionManager.PullLocationsEvent += this.searchConnectionManager_LocationsEvent;
                searchConnectionManager.PullMetadatasRequestEvent += this.searchConnectionManager_MetadatasRequestEvent;
                searchConnectionManager.PullMetadatasEvent += this.searchConnectionManager_MetadatasEvent;
                searchConnectionManager.PullCancelEvent += this.searchConnectionManager_PullCancelEvent;
                searchConnectionManager.CloseEvent += this.searchConnectionManager_CloseEvent;

                _nodeToUri.Add(searchConnectionManager.Node, uri);
                _searchConnectionManagers.Add(searchConnectionManager);

                {
                    var tempMessageManager = _messagesManager[searchConnectionManager.Node];

                    if (tempMessageManager.SessionId != null
                        && !CollectionUtilities.Equals(tempMessageManager.SessionId, searchConnectionManager.SesstionId))
                    {
                        _messagesManager.Remove(searchConnectionManager.Node);
                    }
                }

                var messageManager = _messagesManager[searchConnectionManager.Node];
                messageManager.SessionId = searchConnectionManager.SesstionId;
                messageManager.LastPullTime = DateTime.UtcNow;

                Task.Factory.StartNew(this.ConnectionManagerThread, searchConnectionManager, TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent);
            }
        }

        private void RemoveConnectionManager(SearchConnectionManager searchConnectionManager)
        {
            lock (this.ThisLock)
            {
                lock (_searchConnectionManagers.ThisLock)
                {
                    try
                    {
                        if (_searchConnectionManagers.Contains(searchConnectionManager))
                        {
                            Debug.WriteLine("SearchConnectionManager: Close");

                            _sentByteCount += searchConnectionManager.SentByteCount;
                            _receivedByteCount += searchConnectionManager.ReceivedByteCount;

                            var messageManager = _messagesManager[searchConnectionManager.Node];
                            messageManager.SentByteCount.Add(searchConnectionManager.SentByteCount);
                            messageManager.ReceivedByteCount.Add(searchConnectionManager.ReceivedByteCount);

                            _nodeToUri.Remove(searchConnectionManager.Node);
                            _searchConnectionManagers.Remove(searchConnectionManager);

                            searchConnectionManager.Dispose();
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
                        connectionCount = _searchConnectionManagers.Count(n => n.Direction == ConnectDirection.Out);
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
                        .Where(n => !_searchConnectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
                            && !_creatingNodes.Contains(n)
                            && !_waitingNodes.Contains(n))
                        .Randomize()
                        .FirstOrDefault();

                    if (node == null)
                    {
                        node = _routeTable
                            .ToArray()
                            .Where(n => !_searchConnectionManagers.Any(m => CollectionUtilities.Equals(m.Node.Id, n.Id))
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

                        var connection = this.CreateConnection(uri);

                        if (connection != null)
                        {
                            var searchConnectionManager = new SearchConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.Out, _bufferManager);

                            try
                            {
                                searchConnectionManager.Connect();
                                if (!SearchManager.Check(searchConnectionManager.Node)) throw new ArgumentException();

                                _succeededUris.Add(uri);

                                lock (this.ThisLock)
                                {
                                    _cuttingNodes.Remove(node);

                                    if (node != searchConnectionManager.Node)
                                    {
                                        this.RemoveNode(searchConnectionManager.Node);
                                    }

                                    if (searchConnectionManager.Node.Uris.Count() != 0)
                                    {
                                        _routeTable.Live(searchConnectionManager.Node);
                                    }
                                }

                                _connectConnectionCount.Increment();

                                this.AddConnectionManager(searchConnectionManager, uri);

                                goto End;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine(e);

                                searchConnectionManager.Dispose();
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
                        connectionCount = _searchConnectionManagers.Count(n => n.Direction == ConnectDirection.In);
                    }

                    if (connectionCount >= ((this.ConnectionCountLimit + 1) / 2))
                    {
                        continue;
                    }
                }

                string uri;
                var connection = this.AcceptConnection(out uri);

                if (connection != null)
                {
                    var searchConnectionManager = new SearchConnectionManager(connection, _mySessionId, this.BaseNode, ConnectDirection.In, _bufferManager);

                    try
                    {
                        searchConnectionManager.Connect();
                        if (!SearchManager.Check(searchConnectionManager.Node) || _removeNodes.Contains(searchConnectionManager.Node)) throw new ArgumentException();

                        lock (this.ThisLock)
                        {
                            if (searchConnectionManager.Node.Uris.Count() != 0)
                            {
                                _routeTable.Add(searchConnectionManager.Node);
                            }

                            _cuttingNodes.Remove(searchConnectionManager.Node);
                        }

                        this.AddConnectionManager(searchConnectionManager, uri);

                        _acceptConnectionCount.Increment();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);

                        searchConnectionManager.Dispose();
                    }
                }
            }
        }

        public Connection CreateConnection(string uri)
        {
            var garbages = new List<IDisposable>();

            try
            {
                Connection connection = null;
                {
                    var cap = this.OnCreateCapEvent(uri);
                    if (cap == null) goto End;

                    garbages.Add(cap);

                    connection = new BaseConnection(cap, _bandwidthLimit, _maxReceiveCount, _bufferManager);
                    garbages.Add(connection);

                    End:;
                }

                if (connection == null) return null;

                var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
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

        public Connection AcceptConnection(out string uri)
        {
            uri = null;
            var garbages = new List<IDisposable>();

            try
            {
                Connection connection = null;
                {
                    // Overlay network
                    var cap = this.OnAcceptCapEvent(out uri);
                    if (cap == null) return null;

                    garbages.Add(cap);

                    connection = new BaseConnection(cap, _bandwidthLimit, _maxReceiveCount, _bufferManager);
                    garbages.Add(connection);
                }

                if (connection == null) return null;

                var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
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

        private class NodeSortItem
        {
            public Node Node { get; set; }
            public DateTime LastPullTime { get; set; }
        }

        private volatile bool _refreshThreadRunning;

        private void SearchManagerThread()
        {
            var connectionCheckStopwatch = new Stopwatch();
            connectionCheckStopwatch.Start();

            var refreshStopwatch = new Stopwatch();

            var pushUploadStopwatch = new Stopwatch();
            pushUploadStopwatch.Start();
            var pushDownloadStopwatch = new Stopwatch();
            pushDownloadStopwatch.Start();

            // 電子署名を検証して破損しているMetadataを検索し、削除。
            {
                {
                    // Link
                    {
                        var removeSignatures = new List<string>();

                        foreach (var signature in _settings.GetLinkSignatures().ToArray())
                        {
                            Metadata tempMetadata = _settings.GetLinkMetadata(signature);
                            if (tempMetadata == null) continue;

                            if (!tempMetadata.VerifyCertificate()) removeSignatures.Add(signature);
                        }

                        _settings.RemoveLinkSignatures(removeSignatures);
                    }

                    // Store
                    {
                        var removeSignatures = new List<string>();

                        foreach (var signature in _settings.GetStoreSignatures().ToArray())
                        {
                            Metadata tempMetadata = _settings.GetStoreMetadata(signature);
                            if (tempMetadata == null) continue;

                            if (!tempMetadata.VerifyCertificate()) removeSignatures.Add(signature);
                        }

                        _settings.RemoveStoreSignatures(removeSignatures);
                    }
                }
            }

            for (;;)
            {
                Thread.Sleep(1000);
                if (this.State == ManagerState.Stop) return;

                var connectionCount = 0;

                lock (this.ThisLock)
                {
                    connectionCount = _searchConnectionManagers.Count;
                }

                if (connectionCount > ((this.ConnectionCountLimit / 3) * 1)
                    && connectionCheckStopwatch.Elapsed.TotalMinutes >= 5)
                {
                    connectionCheckStopwatch.Restart();

                    var nodeSortItems = new List<NodeSortItem>();

                    lock (this.ThisLock)
                    {
                        foreach (var searchConnectionManager in _searchConnectionManagers)
                        {
                            nodeSortItems.Add(new NodeSortItem()
                            {
                                Node = searchConnectionManager.Node,
                                LastPullTime = _messagesManager[searchConnectionManager.Node].LastPullTime,
                            });
                        }
                    }

                    nodeSortItems.Sort((x, y) =>
                    {
                        return x.LastPullTime.CompareTo(y.LastPullTime);
                    });

                    foreach (var node in nodeSortItems.Select(n => n.Node).Take(1))
                    {
                        SearchConnectionManager searchConnectionManager = null;

                        lock (this.ThisLock)
                        {
                            searchConnectionManager = _searchConnectionManagers.FirstOrDefault(n => n.Node == node);
                        }

                        if (searchConnectionManager != null)
                        {
                            try
                            {
                                lock (this.ThisLock)
                                {
                                    this.RemoveNode(searchConnectionManager.Node);
                                }

                                searchConnectionManager.PushCancel();

                                Debug.WriteLine("SearchConnectionManager: Push Cancel");
                            }
                            catch (Exception)
                            {

                            }

                            this.RemoveConnectionManager(searchConnectionManager);
                        }
                    }
                }

                if (!refreshStopwatch.IsRunning || refreshStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    refreshStopwatch.Restart();

                    // トラストにより必要なMetadataを選択し、不要なMetadataを削除する。
                    //　非トラストなMetadataでアクセスが頻繁なMetadataを優先して保護する。
                    Task.Run(() =>
                    {
                        if (_refreshThreadRunning) return;
                        _refreshThreadRunning = true;

                        try
                        {
                            var lockSignatures = this.OnLockSignaturesEvent();

                            if (lockSignatures != null)
                            {
                                // Link
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.GetLinkSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _linkMetadataLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.RemoveLinkSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.GetLinkSignatures());

                                    foreach (var signature in _linkMetadataLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _linkMetadataLastAccessTimes.Remove(signature);
                                    }
                                }

                                // Store
                                {
                                    var removeSignatures = new HashSet<string>();
                                    removeSignatures.UnionWith(_settings.GetStoreSignatures());
                                    removeSignatures.ExceptWith(lockSignatures);

                                    var sortList = removeSignatures
                                        .OrderBy(n =>
                                        {
                                            DateTime t;
                                            _storeMetadataLastAccessTimes.TryGetValue(n, out t);

                                            return t;
                                        }).ToList();

                                    _settings.RemoveStoreSignatures(sortList.Take(sortList.Count - 1024));

                                    var liveSignatures = new HashSet<string>(_settings.GetStoreSignatures());

                                    foreach (var signature in _storeMetadataLastAccessTimes.Keys.ToArray())
                                    {
                                        if (liveSignatures.Contains(signature)) continue;

                                        _storeMetadataLastAccessTimes.Remove(signature);
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
                if (pushUploadStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    pushUploadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_searchConnectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    foreach (var key in _locationsManager.GetKeys())
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
                                messageManagers[requestNodes[i]].PullLocationsRequest.Add(key);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var signature in _settings.GetLinkSignatures())
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
                                messageManagers[requestNodes[i]].PullLinkMetadatasRequest.Add(signature, DateTime.MinValue);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }

                    foreach (var signature in _settings.GetStoreSignatures())
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
                                messageManagers[requestNodes[i]].PullStoreMetadatasRequest.Add(signature, DateTime.MinValue);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }

                // ダウンロード
                if (pushDownloadStopwatch.Elapsed.TotalSeconds >= 30)
                {
                    pushDownloadStopwatch.Restart();

                    var baseNode = this.BaseNode;

                    var otherNodes = new List<Node>();

                    lock (this.ThisLock)
                    {
                        otherNodes.AddRange(_searchConnectionManagers.Select(n => n.Node));
                    }

                    var messageManagers = new Dictionary<Node, MessageManager>();

                    foreach (var node in otherNodes)
                    {
                        messageManagers[node] = _messagesManager[node];
                    }

                    var pushLocationsRequestList = new HashSet<Key>();
                    var pushLinkMetadatasRequestList = new HashSet<string>();
                    var pushStoreMetadatasRequestList = new HashSet<string>();

                    {
                        // Location
                        {
                            {
                                var array = _pushLocationsRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushLocationsRequestList.Add(array[i]);

                                    count--;
                                }
                            }

                            foreach (var pair in messageManagers)
                            {
                                var node = pair.Key;
                                var messageManager = pair.Value;

                                {
                                    var array = messageManager.PullLocationsRequest.ToArray();
                                    _random.Shuffle(array);

                                    int count = _maxMetadataRequestCount;

                                    for (int i = 0; count > 0 && i < array.Length; i++)
                                    {
                                        pushLocationsRequestList.Add(array[i]);

                                        count--;
                                    }
                                }
                            }
                        }

                        // Link
                        {
                            {
                                var array = _pushLinkMetadatasRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushLinkMetadatasRequestList.Add(array[i]);

                                    count--;
                                }
                            }

                            foreach (var pair in messageManagers)
                            {
                                var node = pair.Key;
                                var messageManager = pair.Value;

                                {
                                    var array = messageManager.PullLinkMetadatasRequest.ToArray();
                                    _random.Shuffle(array);

                                    int count = _maxMetadataRequestCount;

                                    for (int i = 0; count > 0 && i < array.Length; i++)
                                    {
                                        pushLinkMetadatasRequestList.Add(array[i].Key);

                                        count--;
                                    }
                                }
                            }
                        }

                        // Store
                        {
                            {
                                var array = _pushStoreMetadatasRequestList.ToArray();
                                _random.Shuffle(array);

                                int count = _maxMetadataRequestCount;

                                for (int i = 0; count > 0 && i < array.Length; i++)
                                {
                                    pushStoreMetadatasRequestList.Add(array[i]);

                                    count--;
                                }
                            }

                            foreach (var pair in messageManagers)
                            {
                                var node = pair.Key;
                                var messageManager = pair.Value;

                                {
                                    var array = messageManager.PullStoreMetadatasRequest.ToArray();
                                    _random.Shuffle(array);

                                    int count = _maxMetadataRequestCount;

                                    for (int i = 0; count > 0 && i < array.Length; i++)
                                    {
                                        pushStoreMetadatasRequestList.Add(array[i].Key);

                                        count--;
                                    }
                                }
                            }
                        }
                    }

                    {
                        // Location
                        {
                            var pushLocationsRequestDictionary = new Dictionary<Node, HashSet<Key>>();

                            foreach (var key in pushLocationsRequestList.Randomize())
                            {
                                try
                                {
                                    var requestNodes = new List<Node>();

                                    foreach (var node in Kademlia<Node>.Search(key.Hash, otherNodes, 2))
                                    {
                                        requestNodes.Add(node);
                                    }

                                    for (int i = 0; i < requestNodes.Count; i++)
                                    {
                                        HashSet<Key> collection;

                                        if (!pushLocationsRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                        {
                                            collection = new HashSet<Key>();
                                            pushLocationsRequestDictionary[requestNodes[i]] = collection;
                                        }

                                        if (collection.Count < _maxMetadataRequestCount)
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

                            lock (_pushLocationsRequestDictionary.ThisLock)
                            {
                                _pushLocationsRequestDictionary.Clear();

                                foreach (var pair in pushLocationsRequestDictionary)
                                {
                                    var node = pair.Key;
                                    var targets = pair.Value;

                                    _pushLocationsRequestDictionary.Add(node, new List<Key>(targets.Randomize()));
                                }
                            }
                        }

                        // Link
                        {
                            var pushLinkMetadatasRequestDictionary = new Dictionary<Node, HashSet<string>>();

                            foreach (var signature in pushLinkMetadatasRequestList.Randomize())
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

                                        if (!pushLinkMetadatasRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                        {
                                            collection = new HashSet<string>();
                                            pushLinkMetadatasRequestDictionary[requestNodes[i]] = collection;
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

                            lock (_pushLinkMetadatasRequestDictionary.ThisLock)
                            {
                                _pushLinkMetadatasRequestDictionary.Clear();

                                foreach (var pair in pushLinkMetadatasRequestDictionary)
                                {
                                    var node = pair.Key;
                                    var targets = pair.Value;

                                    _pushLinkMetadatasRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                                }
                            }
                        }

                        // Store
                        {
                            var pushStoreMetadatasRequestDictionary = new Dictionary<Node, HashSet<string>>();

                            foreach (var signature in pushStoreMetadatasRequestList.Randomize())
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

                                        if (!pushStoreMetadatasRequestDictionary.TryGetValue(requestNodes[i], out collection))
                                        {
                                            collection = new HashSet<string>();
                                            pushStoreMetadatasRequestDictionary[requestNodes[i]] = collection;
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

                            lock (_pushStoreMetadatasRequestDictionary.ThisLock)
                            {
                                _pushStoreMetadatasRequestDictionary.Clear();

                                foreach (var pair in pushStoreMetadatasRequestDictionary)
                                {
                                    var node = pair.Key;
                                    var targets = pair.Value;

                                    _pushStoreMetadatasRequestDictionary.Add(node, new List<string>(targets.Randomize()));
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ConnectionManagerThread(object state)
        {
            Thread.CurrentThread.Name = "SearchManager_ConnectionManagerThread";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;

            var searchConnectionManager = state as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            try
            {
                var messageManager = _messagesManager[searchConnectionManager.Node];

                var nodeUpdateTime = new Stopwatch();
                var updateTime = new Stopwatch();
                updateTime.Start();
                var locationUpdateTime = new Stopwatch();
                locationUpdateTime.Start();
                var metadataUpdateTime = new Stopwatch();
                metadataUpdateTime.Start();

                for (;;)
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;
                    if (!_searchConnectionManagers.Contains(searchConnectionManager)) return;

                    var connectionCount = 0;

                    lock (this.ThisLock)
                    {
                        connectionCount = _searchConnectionManagers.Count;
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
                            searchConnectionManager.PushNodes(nodes.Randomize());

                            Debug.WriteLine(string.Format("SearchConnectionManager: Push Nodes ({0})", nodes.Count));
                            _pushNodeCount.Add(nodes.Count);
                        }
                    }

                    if (updateTime.Elapsed.TotalSeconds >= 30)
                    {
                        updateTime.Restart();

                        // PushLocationsRequest
                        {
                            List<Key> targetList = null;

                            lock (_pushLocationsRequestDictionary.ThisLock)
                            {
                                if (_pushLocationsRequestDictionary.TryGetValue(searchConnectionManager.Node, out targetList))
                                {
                                    _pushLocationsRequestDictionary.Remove(searchConnectionManager.Node);
                                }
                            }

                            if (targetList != null)
                            {
                                searchConnectionManager.PushLocationsRequest(targetList);

                                foreach (var item in targetList)
                                {
                                    _pushLocationsRequestList.Remove(item);
                                }

                                Debug.WriteLine(string.Format("SearchConnectionManager: Push LocationsRequest ({0})", targetList.Count));
                                _pushLocationRequestCount.Add(targetList.Count);
                            }
                        }

                        // PushMetadatasRequest
                        {
                            var queryList = new List<QueryMetadata>();

                            {
                                // Link
                                {
                                    var targetList = new List<string>();

                                    lock (_pushLinkMetadatasRequestDictionary.ThisLock)
                                    {
                                        if (_pushLinkMetadatasRequestDictionary.TryGetValue(searchConnectionManager.Node, out targetList))
                                        {
                                            _pushLinkMetadatasRequestDictionary.Remove(searchConnectionManager.Node);
                                        }
                                    }

                                    foreach (var signature in targetList)
                                    {
                                        var metadata = _settings.GetLinkMetadata(signature);
                                        queryList.Add(new QueryMetadata(MetadataType.Link, metadata?.CreationTime ?? DateTime.MinValue, signature));
                                    }
                                }

                                // Store
                                {
                                    var targetList = new List<string>();

                                    lock (_pushStoreMetadatasRequestDictionary.ThisLock)
                                    {
                                        if (_pushStoreMetadatasRequestDictionary.TryGetValue(searchConnectionManager.Node, out targetList))
                                        {
                                            _pushStoreMetadatasRequestDictionary.Remove(searchConnectionManager.Node);
                                        }
                                    }

                                    foreach (var signature in targetList)
                                    {
                                        var metadata = _settings.GetStoreMetadata(signature);
                                        queryList.Add(new QueryMetadata(MetadataType.Store, metadata?.CreationTime ?? DateTime.MinValue, signature));
                                    }
                                }
                            }

                            if (queryList.Count > 0)
                            {
                                searchConnectionManager.PushMetadatasRequest(queryList);

                                foreach (var item in queryList)
                                {
                                    if (item.Type == MetadataType.Link)
                                    {
                                        _pushLinkMetadatasRequestList.Remove(item.Signature);
                                    }
                                    else if (item.Type == MetadataType.Link)
                                    {
                                        _pushStoreMetadatasRequestList.Remove(item.Signature);
                                    }
                                }

                                Debug.WriteLine(string.Format("SearchConnectionManager: Push SeedsRequest ({0})", queryList.Count));
                                _pushMetadataRequestCount.Add(queryList.Count);
                            }
                        }
                    }

                    if (locationUpdateTime.Elapsed.TotalSeconds >= 30)
                    {
                        locationUpdateTime.Restart();

                        // PushLocations
                        {
                            var locations = new List<Location>();

                            // Link
                            foreach (var key in messageManager.PullLocationsRequest.Randomize())
                            {
                                var count = _maxLocationCount - locations.Count;
                                if (count <= 0) break;

                                locations.AddRange(_locationsManager.GetLocations(key).Take(count));
                            }

                            if (locations.Count > 0)
                            {
                                _random.Shuffle(locations);

                                searchConnectionManager.PushLocations(locations);

                                Debug.WriteLine(string.Format("SearchConnectionManager: Push Locations ({0})", locations.Count));
                                _pushLocationCount.Add(locations.Count);
                            }
                        }
                    }

                    if (metadataUpdateTime.Elapsed.TotalSeconds >= 30)
                    {
                        metadataUpdateTime.Restart();

                        // PushMetadatas
                        {
                            var linkMetadatas = new List<Metadata>();

                            // Link
                            foreach (var pair in messageManager.PullLinkMetadatasRequest.Randomize())
                            {
                                Metadata tempMetadata = _settings.GetLinkMetadata(pair.Key);
                                if (tempMetadata == null) continue;

                                if (tempMetadata.CreationTime > pair.Value)
                                {
                                    linkMetadatas.Add(tempMetadata);

                                    if (linkMetadatas.Count >= (_maxMetadataCount / 2)) break;
                                }
                            }

                            var storeMetadatas = new List<Metadata>();

                            // Store
                            foreach (var pair in messageManager.PullStoreMetadatasRequest.Randomize())
                            {
                                Metadata tempMetadata = _settings.GetStoreMetadata(pair.Key);
                                if (tempMetadata == null) continue;

                                if (tempMetadata.CreationTime > pair.Value)
                                {
                                    storeMetadatas.Add(tempMetadata);

                                    if (storeMetadatas.Count >= (_maxMetadataCount / 2)) break;
                                }
                            }

                            if (linkMetadatas.Count > 0 || storeMetadatas.Count > 0)
                            {
                                var metadatas = new List<Metadata>();
                                metadatas.AddRange(linkMetadatas);
                                metadatas.AddRange(storeMetadatas);

                                _random.Shuffle(metadatas);

                                searchConnectionManager.PushMetadatas(metadatas);

                                Debug.WriteLine(string.Format("SearchConnectionManager: Push Metadatas ({0})", metadatas.Count));
                                _pushMetadataCount.Add(metadatas.Count);
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
                this.RemoveConnectionManager(searchConnectionManager);
            }
        }

        #region searchConnectionManager_Event

        private void searchConnectionManager_NodesEvent(object sender, PullNodesEventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            Debug.WriteLine(string.Format("SearchConnectionManager: Pull Nodes ({0})", e.Nodes.Count()));

            foreach (var node in e.Nodes.Take(_maxNodeCount))
            {
                if (!SearchManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                _routeTable.Add(node);
                _pullNodeCount.Increment();
            }
        }

        private void searchConnectionManager_LocationsRequestEvent(object sender, PullLocationsRequestEventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            var messageManager = _messagesManager[searchConnectionManager.Node];

            if (messageManager.PullLocationsRequest.Count > _maxLocationRequestCount * messageManager.PullLocationsRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("SearchConnectionManager: Pull LocationsRequest ({0})", e.Keys.Count()));

            foreach (var key in e.Keys.Take(_maxLocationRequestCount))
            {
                if (!SearchManager.Check(key)) continue;

                messageManager.PullLocationsRequest.Add(key);

                _pullLocationRequestCount.Increment();
            }
        }

        private void searchConnectionManager_LocationsEvent(object sender, PullLocationsEventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            var messageManager = _messagesManager[searchConnectionManager.Node];

            if (messageManager.PullLocationsCount.Get() > _maxLocationCount * messageManager.PullLocationsCount.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("SearchConnectionManager: Pull Locations ({0})", e.Locations.Count()));

            foreach (var location in e.Locations.Take(_maxLocationCount))
            {
                _locationsManager.SetLocation(location);
                _pullLocationCount.Increment();
            }

            messageManager.PullLocationsCount.Add(e.Locations.Count());
        }

        private void searchConnectionManager_MetadatasRequestEvent(object sender, PullMetadatasRequestEventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            var messageManager = _messagesManager[searchConnectionManager.Node];

            if (messageManager.PullLinkMetadatasRequest.Count > (_maxMetadataRequestCount / 2) * messageManager.PullLinkMetadatasRequest.SurvivalTime.TotalMinutes) return;
            if (messageManager.PullStoreMetadatasRequest.Count > (_maxMetadataRequestCount / 2) * messageManager.PullStoreMetadatasRequest.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("SearchConnectionManager: Pull MetadatasRequest ({0})", e.QueryMetadatas.Count()));

            foreach (var queryMetadata in e.QueryMetadatas.Take(_maxMetadataRequestCount))
            {
                if (!SearchManager.Check(queryMetadata.Signature)) continue;

                if (queryMetadata.Type == MetadataType.Link)
                {
                    messageManager.PullLinkMetadatasRequest[queryMetadata.Signature] = queryMetadata.CreationTime;
                    _linkMetadataLastAccessTimes[queryMetadata.Signature] = DateTime.UtcNow;
                }
                else if (queryMetadata.Type == MetadataType.Store)
                {
                    messageManager.PullStoreMetadatasRequest[queryMetadata.Signature] = queryMetadata.CreationTime;
                    _storeMetadataLastAccessTimes[queryMetadata.Signature] = DateTime.UtcNow;
                }

                _pullMetadataRequestCount.Increment();
            }
        }

        private void searchConnectionManager_MetadatasEvent(object sender, PullMetadatasEventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            var messageManager = _messagesManager[searchConnectionManager.Node];

            if (messageManager.PullMetadatasCount.Get() > _maxMetadataCount * messageManager.PullMetadatasCount.SurvivalTime.TotalMinutes) return;

            Debug.WriteLine(string.Format("SearchConnectionManager: Pull Metadatas ({0})", e.Metadatas.Count()));

            foreach (var metadata in e.Metadatas.Take(_maxMetadataCount))
            {
                if (_settings.SetLinkMetadata(metadata))
                {
                    var signature = metadata.Certificate.ToString();

                    _linkMetadataLastAccessTimes[signature] = DateTime.UtcNow;
                }
                else if (_settings.SetStoreMetadata(metadata))
                {
                    var signature = metadata.Certificate.ToString();

                    _storeMetadataLastAccessTimes[signature] = DateTime.UtcNow;
                }

                _pullMetadataCount.Increment();
            }

            messageManager.PullMetadatasCount.Add(e.Metadatas.Count());
        }

        private void searchConnectionManager_PullCancelEvent(object sender, EventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            Debug.WriteLine("SearchConnectionManager: Pull Cancel");

            try
            {
                lock (this.ThisLock)
                {
                    this.RemoveNode(searchConnectionManager.Node);
                }

                this.RemoveConnectionManager(searchConnectionManager);
            }
            catch (Exception)
            {

            }
        }

        private void searchConnectionManager_CloseEvent(object sender, EventArgs e)
        {
            var searchConnectionManager = sender as SearchConnectionManager;
            if (searchConnectionManager == null) return;

            try
            {
                lock (this.ThisLock)
                {
                    if (!_removeNodes.Contains(searchConnectionManager.Node))
                    {
                        _cuttingNodes.Add(searchConnectionManager.Node);
                    }
                }

                this.RemoveConnectionManager(searchConnectionManager);
            }
            catch (Exception)
            {

            }
        }

        #endregion

        public void SetBaseNode(Node baseNode)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!SearchManager.Check(baseNode)) throw new ArgumentException("baseNode");

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
                    if (!SearchManager.Check(node) || node.Uris.Count() == 0 || _removeNodes.Contains(node)) continue;

                    _routeTable.Add(node);
                }
            }
        }

        public IEnumerable<Location> GetLocation(Key key)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushLocationsRequestList.Add(key);

                return _locationsManager.GetLocations(key);
            }
        }

        public Metadata GetLinkMetadata(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushLinkMetadatasRequestList.Add(signature);

                return _settings.GetLinkMetadata(signature);
            }
        }

        public Metadata GetStoreMetadata(string signature)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _pushStoreMetadatasRequestList.Add(signature);

                return _settings.GetStoreMetadata(signature);
            }
        }

        public void Upload(Metadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                _settings.SetLinkMetadata(metadata);
                _settings.SetStoreMetadata(metadata);
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

                    _searchManagerThread = new Thread(this.SearchManagerThread);
                    _searchManagerThread.Name = "SearchManager_SearchManagerThread";
                    _searchManagerThread.Priority = ThreadPriority.Lowest;
                    _searchManagerThread.Start();

                    for (int i = 0; i < 3; i++)
                    {
                        var thread = new Thread(this.CreateConnectionThread);
                        thread.Name = "SearchManager_CreateConnectionThread";
                        thread.Priority = ThreadPriority.Lowest;
                        thread.Start();

                        _createConnectionThreads.Add(thread);
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        var thread = new Thread(this.AcceptConnectionThread);
                        thread.Name = "SearchManager_AcceptConnectionThread";
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

                _searchManagerThread.Join();
                _searchManagerThread = null;

                lock (this.ThisLock)
                {
                    foreach (var item in _searchConnectionManagers.ToArray())
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
                    if (!SearchManager.Check(node) || node.Uris.Count() == 0) continue;

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

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<Node>() { Name = "BaseNode", Value = new Node(new byte[0], null)},
                    new Library.Configuration.SettingContent<NodeCollection>() { Name = "OtherNodes", Value = new NodeCollection() },
                    new Library.Configuration.SettingContent<int>() { Name = "ConnectionCountLimit", Value = 32 },
                    new Library.Configuration.SettingContent<int>() { Name = "BandwidthLimit", Value = 0 },
                    new Library.Configuration.SettingContent<Dictionary<string, Metadata>>() { Name = "LinkMetadatas", Value = new Dictionary<string, Metadata>() },
                    new Library.Configuration.SettingContent<Dictionary<string, Metadata>>() { Name = "StoreMetadatas", Value = new Dictionary<string, Metadata>() },
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

            public IEnumerable<string> GetLinkSignatures()
            {
                lock (_thisLock)
                {
                    return this.LinkMetadatas.Keys.ToArray();
                }
            }

            public IEnumerable<string> GetStoreSignatures()
            {
                lock (_thisLock)
                {
                    return this.StoreMetadatas.Keys.ToArray();
                }
            }

            public void RemoveLinkSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.LinkMetadatas.Remove(signature);
                    }
                }
            }

            public void RemoveStoreSignatures(IEnumerable<string> signatures)
            {
                lock (_thisLock)
                {
                    foreach (var signature in signatures)
                    {
                        this.StoreMetadatas.Remove(signature);
                    }
                }
            }

            public Metadata GetLinkMetadata(string signature)
            {
                lock (_thisLock)
                {
                    Metadata metadata;

                    if (this.LinkMetadatas.TryGetValue(signature, out metadata))
                    {
                        return metadata;
                    }

                    return null;
                }
            }

            public Metadata GetStoreMetadata(string signature)
            {
                lock (_thisLock)
                {
                    Metadata metadata;

                    if (this.StoreMetadatas.TryGetValue(signature, out metadata))
                    {
                        return metadata;
                    }

                    return null;
                }
            }

            public bool SetLinkMetadata(Metadata metadata)
            {
                var now = DateTime.UtcNow;

                if (metadata == null
                    || metadata.Type == MetadataType.Link
                    || (metadata.CreationTime - now).Minutes > 30) return false;

                if (metadata.Certificate == null) throw new CertificateException();

                var signature = metadata.Certificate.ToString();

                // なるべく電子署名の検証をさけ、CPU使用率を下げるよう工夫する。
                lock (_thisLock)
                {
                    Metadata tempMetadata;

                    if (!this.LinkMetadatas.TryGetValue(signature, out tempMetadata)
                        || metadata.CreationTime > tempMetadata.CreationTime)
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        this.LinkMetadatas[signature] = metadata;
                    }

                    return (tempMetadata == null || metadata.CreationTime >= tempMetadata.CreationTime);
                }
            }

            public bool SetStoreMetadata(Metadata metadata)
            {
                var now = DateTime.UtcNow;

                if (metadata == null
                    || metadata.Type == MetadataType.Store
                    || (metadata.CreationTime - now).Minutes > 30) return false;

                if (metadata.Certificate == null) throw new CertificateException();

                var signature = metadata.Certificate.ToString();

                // なるべく電子署名の検証をさけ、CPU使用率を下げるよう工夫する。
                lock (_thisLock)
                {
                    Metadata tempMetadata;

                    if (!this.StoreMetadatas.TryGetValue(signature, out tempMetadata)
                        || metadata.CreationTime > tempMetadata.CreationTime)
                    {
                        if (!metadata.VerifyCertificate()) throw new CertificateException();

                        this.StoreMetadatas[signature] = metadata;
                    }

                    return (tempMetadata == null || metadata.CreationTime >= tempMetadata.CreationTime);
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

            private Dictionary<string, Metadata> LinkMetadatas
            {
                get
                {
                    return (Dictionary<string, Metadata>)this["LinkMetadatas"];
                }
            }

            private Dictionary<string, Metadata> StoreMetadatas
            {
                get
                {
                    return (Dictionary<string, Metadata>)this["StoreMetadatas"];
                }
            }
        }

        private class LocationsManager : IThisLock
        {
            private LockedHashDictionary<Key, Info> _locations = new LockedHashDictionary<Key, Info>();

            private readonly object _thisLock = new object();

            public LocationsManager()
            {

            }

            public void Refresh()
            {
                lock (this.ThisLock)
                {
                    foreach (var key in _locations.Keys.ToArray())
                    {
                        this.GetLocations(key);
                    }
                }
            }

            public IEnumerable<Key> GetKeys()
            {
                lock (this.ThisLock)
                {
                    return _locations.Keys.ToArray();
                }
            }

            public IEnumerable<Location> GetLocations(Key key)
            {
                lock (this.ThisLock)
                {
                    var list = new List<Location>();

                    Info topInfo;

                    if (_locations.TryGetValue(key, out topInfo))
                    {
                        bool flag = false;

                        {
                            var now = DateTime.UtcNow;

                            Info previousInfo = null;
                            Info currentInfo = topInfo;

                            for (;;)
                            {
                                if ((now - currentInfo.CreationTime).TotalMinutes > 30)
                                {
                                    if (previousInfo == null)
                                    {
                                        topInfo = currentInfo.Next;

                                        flag = true;
                                    }
                                    else
                                    {
                                        previousInfo.Next = currentInfo.Next;
                                    }

                                    currentInfo = currentInfo.Next;
                                }
                                else
                                {
                                    list.Add(currentInfo.Location);

                                    previousInfo = currentInfo;
                                    currentInfo = currentInfo.Next;
                                }

                                if (currentInfo == null) break;
                            }
                        }

                        if (flag)
                        {
                            if (topInfo == null) _locations.Remove(key);
                            else _locations[key] = topInfo;
                        }
                    }

                    return list;
                }
            }

            public void SetLocation(Location location)
            {
                lock (this.ThisLock)
                {
                    Info topInfo;

                    if (!_locations.TryGetValue(location.Key, out topInfo))
                    {
                        topInfo = new Info();
                        topInfo.CreationTime = DateTime.UtcNow;
                        topInfo.Location = location;

                        _locations.Add(location.Key, topInfo);
                    }
                    else
                    {
                        bool flag = false;
                        int count = 1;

                        {
                            var now = DateTime.UtcNow;

                            Info previousInfo = null;
                            Info currentInfo = topInfo;

                            for (;;)
                            {
                                if ((now - currentInfo.CreationTime).TotalMinutes > 30
                                    || currentInfo.Location == location)
                                {
                                    if (previousInfo == null)
                                    {
                                        topInfo = currentInfo.Next;

                                        flag = true;
                                    }
                                    else
                                    {
                                        previousInfo.Next = currentInfo.Next;
                                    }

                                    currentInfo = currentInfo.Next;
                                }
                                else
                                {
                                    previousInfo = currentInfo;
                                    currentInfo = currentInfo.Next;

                                    count++;
                                }

                                if (currentInfo == null) break;
                            }

                            {
                                currentInfo = new Info();
                                currentInfo.CreationTime = now;
                                currentInfo.Location = location;

                                previousInfo.Next = currentInfo;
                            }
                        }

                        if (count > 128)
                        {
                            topInfo = topInfo.Next;

                            flag = true;
                        }

                        if (flag)
                        {
                            if (topInfo == null) _locations.Remove(location.Key);
                            else _locations[location.Key] = topInfo;
                        }
                    }
                }
            }

            // 自前のLinkedListのためのアイテムを用意。
            private sealed class Info
            {
                public DateTime CreationTime { get; set; }
                public Location Location { get; set; }

                public Info Next { get; set; }
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
    class SearchManagerException : ManagerException
    {
        public SearchManagerException() : base() { }
        public SearchManagerException(string message) : base(message) { }
        public SearchManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class CertificateException : SearchManagerException
    {
        public CertificateException() : base() { }
        public CertificateException(string message) : base(message) { }
        public CertificateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
