using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    delegate IEnumerable<Node> GetLockNodesEventHandler(object sender);

    sealed class PacketControlManager : ManagerBase, IThisLock
    {
        private Dictionary<Node, PacketManager> _packetManagerDictionary = new Dictionary<Node, PacketManager>();
        private Dictionary<Node, DateTime> _updateTimeDictionary = new Dictionary<Node, DateTime>();
        private int _id;

        private WatchTimer _refreshTimer;
        private volatile bool _checkedFlag = false;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public GetLockNodesEventHandler GetLockNodesEvent;

        public PacketControlManager()
        {
            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 30));
        }

        private void RefreshTimer()
        {
            lock (this.ThisLock)
            {
                foreach (var packetManager in _packetManagerDictionary.Values.ToArray())
                {
                    packetManager.StockBlocks.TrimExcess();
                    packetManager.StockBroadcastMetadatas.TrimExcess();
                    packetManager.StockUnicastMetadatas.TrimExcess();
                    packetManager.StockMulticastMetadatas.TrimExcess();

                    packetManager.PullBlocksLink.TrimExcess();
                    packetManager.PullBlocksRequest.TrimExcess();
                    packetManager.PullBroadcastMetadatasRequest.TrimExcess();
                    packetManager.PullUnicastMetadatasRequest.TrimExcess();
                    packetManager.PullMulticastMetadatasRequest.TrimExcess();
                }

                if (_packetManagerDictionary.Count > 128)
                {
                    if (_checkedFlag) return;
                    _checkedFlag = true;

                    Task.Run(() =>
                    {
                        var lockedNodes = new HashSet<Node>();

                        if (this.GetLockNodesEvent != null)
                        {
                            lockedNodes.UnionWith(this.GetLockNodesEvent(this));
                        }

                        lock (this.ThisLock)
                        {
                            if (_packetManagerDictionary.Count > 128)
                            {
                                var pairs = _updateTimeDictionary.Where(n => !lockedNodes.Contains(n.Key)).ToList();

                                pairs.Sort((x, y) =>
                                {
                                    return x.Value.CompareTo(y.Value);
                                });

                                foreach (var node in pairs.Select(n => n.Key).Take(_packetManagerDictionary.Count - 128))
                                {
                                    _packetManagerDictionary.Remove(node);
                                    _updateTimeDictionary.Remove(node);
                                }
                            }
                        }

                        _checkedFlag = false;
                    });
                }
            }
        }

        public PacketManager this[Node node]
        {
            get
            {
                lock (this.ThisLock)
                {
                    PacketManager packetManager = null;

                    if (!_packetManagerDictionary.TryGetValue(node, out packetManager))
                    {
                        while (_packetManagerDictionary.Any(n => n.Value.Id == _id)) _id++;

                        packetManager = new PacketManager(_id);
                        _packetManagerDictionary[node] = packetManager;
                    }

                    _updateTimeDictionary[node] = DateTime.UtcNow;

                    return packetManager;
                }
            }
        }

        public void Remove(Node node)
        {
            lock (this.ThisLock)
            {
                _packetManagerDictionary.Remove(node);
                _updateTimeDictionary.Remove(node);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _packetManagerDictionary.Clear();
                _updateTimeDictionary.Clear();
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

    class PacketManager : IThisLock
    {
        private int _id;
        private byte[] _sessionId;
        private readonly SafeInteger _priority;

        private readonly SafeInteger _receivedByteCount;
        private readonly SafeInteger _sentByteCount;

        private DateTime _lastPullTime = DateTime.UtcNow;

        private VolatileHashSet<Key> _stockBlocks;
        private VolatileHashSet<byte[]> _stockBroadcastMetadatas;
        private VolatileHashSet<byte[]> _stockUnicastMetadatas;
        private VolatileHashSet<byte[]> _stockMulticastMetadatas;

        private VolatileHashSet<Key> _pullBlocksLink;
        private VolatileHashSet<Key> _pullBlocksRequest;
        private VolatileHashSet<string> _pullBroadcastMetadatasRequest;
        private VolatileHashSet<string> _pullUnicastMetadatasRequest;
        private VolatileHashSet<Tag> _pullMulticastMetadatasRequest;

        private readonly object _thisLock = new object();

        public PacketManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockBlocks = new VolatileHashSet<Key>(new TimeSpan(1, 0, 0));
            _stockBroadcastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockUnicastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockMulticastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());

            _pullBlocksLink = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBroadcastMetadatasRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullUnicastMetadatasRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullMulticastMetadatasRequest = new VolatileHashSet<Tag>(new TimeSpan(0, 30, 0));
        }

        public int Id
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _id;
                }
            }
        }

        public byte[] SessionId
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _sessionId;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _sessionId = value;
                }
            }
        }

        public SafeInteger Priority
        {
            get
            {
                return _priority;
            }
        }

        public SafeInteger ReceivedByteCount
        {
            get
            {
                return _receivedByteCount;
            }
        }

        public SafeInteger SentByteCount
        {
            get
            {
                return _sentByteCount;
            }
        }

        public DateTime LastPullTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _lastPullTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _lastPullTime = value;
                }
            }
        }

        public VolatileHashSet<Key> StockBlocks
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockBlocks;
                }
            }
        }

        public VolatileHashSet<byte[]> StockBroadcastMetadatas
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockBroadcastMetadatas;
                }
            }
        }

        public VolatileHashSet<byte[]> StockUnicastMetadatas
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockUnicastMetadatas;
                }
            }
        }

        public VolatileHashSet<byte[]> StockMulticastMetadatas
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockMulticastMetadatas;
                }
            }
        }

        public VolatileHashSet<Key> PullBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksLink;
                }
            }
        }

        public VolatileHashSet<Key> PullBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBlocksRequest;
                }
            }
        }

        public VolatileHashSet<string> PullBroadcastMetadatasRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBroadcastMetadatasRequest;
                }
            }
        }

        public VolatileHashSet<string> PullUnicastMetadatasRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullUnicastMetadatasRequest;
                }
            }
        }

        public VolatileHashSet<Tag> PullMulticastMetadatasRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMulticastMetadatasRequest;
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
}
