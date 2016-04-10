using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;

namespace Library.Net.Covenant
{
    delegate IEnumerable<Node> GetLockNodesEventHandler(object sender);

    sealed class MessagesManager : ManagerBase, IThisLock
    {
        private Dictionary<Node, MessageManager> _messageManagerDictionary = new Dictionary<Node, MessageManager>();
        private Dictionary<Node, DateTime> _updateTimeDictionary = new Dictionary<Node, DateTime>();
        private int _id;

        private WatchTimer _refreshTimer;
        private volatile bool _checkedFlag = false;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public GetLockNodesEventHandler GetLockNodesEvent;

        public MessagesManager()
        {
            _refreshTimer = new WatchTimer(this.RefreshTimer, new TimeSpan(0, 0, 30));
        }

        private void RefreshTimer()
        {
            lock (this.ThisLock)
            {
                foreach (var messageManager in _messageManagerDictionary.Values.ToArray())
                {
                    messageManager.StockMetadatas.TrimExcess();
                    messageManager.StockLocations.TrimExcess();

                    messageManager.PushProfilesRequest.TrimExcess();
                    messageManager.PullProfilesRequest.TrimExcess();

                    messageManager.PushMetadatasRequest.TrimExcess();
                    messageManager.PullMetadatasRequest.TrimExcess();

                    messageManager.PushLocationsRequest.TrimExcess();
                    messageManager.PullLocationsRequest.TrimExcess();
                }

                if (_messageManagerDictionary.Count > 128)
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
                            if (_messageManagerDictionary.Count > 128)
                            {
                                var pairs = _updateTimeDictionary.Where(n => !lockedNodes.Contains(n.Key)).ToList();

                                pairs.Sort((x, y) =>
                                {
                                    return x.Value.CompareTo(y.Value);
                                });

                                foreach (var node in pairs.Select(n => n.Key).Take(_messageManagerDictionary.Count - 128))
                                {
                                    _messageManagerDictionary.Remove(node);
                                    _updateTimeDictionary.Remove(node);
                                }
                            }
                        }

                        _checkedFlag = false;
                    });
                }
            }
        }

        public MessageManager this[Node node]
        {
            get
            {
                lock (this.ThisLock)
                {
                    MessageManager messageManager = null;

                    if (!_messageManagerDictionary.TryGetValue(node, out messageManager))
                    {
                        while (_messageManagerDictionary.Any(n => n.Value.Id == _id)) _id++;

                        messageManager = new MessageManager(_id);
                        _messageManagerDictionary[node] = messageManager;
                    }

                    _updateTimeDictionary[node] = DateTime.UtcNow;

                    return messageManager;
                }
            }
        }

        public void Remove(Node node)
        {
            lock (this.ThisLock)
            {
                _messageManagerDictionary.Remove(node);
                _updateTimeDictionary.Remove(node);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _messageManagerDictionary.Clear();
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

    class MessageManager : IThisLock
    {
        private int _id;
        private byte[] _sessionId;
        private readonly SafeInteger _priority;

        private readonly SafeInteger _receivedByteCount;
        private readonly SafeInteger _sentByteCount;

        private DateTime _lastPullTime = DateTime.UtcNow;

        private VolatileHashSet<byte[]> _stockMetadatas;
        private VolatileHashSet<byte[]> _stockLocations;

        private VolatileHashDictionary<string, DateTime> _pushProfilesRequest;
        private VolatileHashDictionary<string, DateTime> _pullProfilesRequest;

        private VolatileHashSet<string> _pushMetadataRequest;
        private VolatileHashSet<string> _pullMetadatasRequest;

        private VolatileHashSet<Key> _pushLocationsRequest;
        private VolatileHashSet<Key> _pullLocationsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockLocations = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());

            _pushProfilesRequest = new VolatileHashDictionary<string, DateTime>(new TimeSpan(0, 30, 0));
            _pullProfilesRequest = new VolatileHashDictionary<string, DateTime>(new TimeSpan(0, 30, 0));

            _pushMetadataRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullMetadatasRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));

            _pushLocationsRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullLocationsRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
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

        public VolatileHashSet<byte[]> StockMetadatas
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockMetadatas;
                }
            }
        }

        public VolatileHashSet<byte[]> StockLocations
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockLocations;
                }
            }
        }

        public VolatileHashDictionary<string, DateTime> PushProfilesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushProfilesRequest;
                }
            }
        }

        public VolatileHashDictionary<string, DateTime> PullProfilesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullProfilesRequest;
                }
            }
        }

        public VolatileHashSet<string> PushMetadatasRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMetadataRequest;
                }
            }
        }

        public VolatileHashSet<string> PullMetadatasRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMetadatasRequest;
                }
            }
        }

        public VolatileHashSet<Key> PushLocationsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushLocationsRequest;
                }
            }
        }

        public VolatileHashSet<Key> PullLocationsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullLocationsRequest;
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
