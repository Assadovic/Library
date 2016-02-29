﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Net.Outopos
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
                    messageManager.StockBlocks.TrimExcess();
                    messageManager.StockBroadcastMetadatas.TrimExcess();
                    messageManager.StockUnicastMetadatas.TrimExcess();
                    messageManager.StockWikiDocumentMetadatas.TrimExcess();
                    messageManager.StockMulticastMetadatas.TrimExcess();

                    messageManager.PushBlocksLink.TrimExcess();
                    messageManager.PullBlocksLink.TrimExcess();

                    messageManager.PushBlocksRequest.TrimExcess();
                    messageManager.PullBlocksRequest.TrimExcess();

                    messageManager.PushBroadcastSignaturesRequest.TrimExcess();
                    messageManager.PullBroadcastSignaturesRequest.TrimExcess();

                    messageManager.PushUnicastSignaturesRequest.TrimExcess();
                    messageManager.PullUnicastSignaturesRequest.TrimExcess();

                    messageManager.PushMulticastTagsRequest.TrimExcess();
                    messageManager.PullMulticastTagsRequest.TrimExcess();
                }

                if (_messageManagerDictionary.Count > 128)
                {
                    if (_checkedFlag) return;
                    _checkedFlag = true;

                    ThreadPool.QueueUserWorkItem((object wstate) =>
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

        private VolatileHashSet<Key> _stockBlocks;
        private VolatileHashSet<byte[]> _stockBroadcastMetadatas;
        private VolatileHashSet<byte[]> _stockUnicastMetadatas;
        private VolatileHashSet<byte[]> _stockWikiDocumentMetadatas;
        private VolatileHashSet<byte[]> _stockMulticastMetadatas;

        private VolatileHashSet<Key> _pushBlocksLink;
        private VolatileHashSet<Key> _pullBlocksLink;

        private VolatileHashSet<Key> _pushBlocksRequest;
        private VolatileHashSet<Key> _pullBlocksRequest;

        private VolatileHashSet<string> _pushBroadcastSignaturesRequest;
        private VolatileHashSet<string> _pullBroadcastSignaturesRequest;

        private VolatileHashSet<string> _pushUnicastSignaturesRequest;
        private VolatileHashSet<string> _pullUnicastSignaturesRequest;

        private VolatileHashSet<Tag> _pushMulticastTagsRequest;
        private VolatileHashSet<Tag> _pullMulticastTagsRequest;

        private readonly object _thisLock = new object();

        public MessageManager(int id)
        {
            _id = id;

            _priority = new SafeInteger();

            _receivedByteCount = new SafeInteger();
            _sentByteCount = new SafeInteger();

            _stockBlocks = new VolatileHashSet<Key>(new TimeSpan(1, 0, 0));
            _stockBroadcastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockUnicastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockWikiDocumentMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());
            _stockMulticastMetadatas = new VolatileHashSet<byte[]>(new TimeSpan(1, 0, 0), new ByteArrayEqualityComparer());

            _pushBlocksLink = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksLink = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _pushBlocksRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));
            _pullBlocksRequest = new VolatileHashSet<Key>(new TimeSpan(0, 30, 0));

            _pushBroadcastSignaturesRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullBroadcastSignaturesRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));

            _pushUnicastSignaturesRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));
            _pullUnicastSignaturesRequest = new VolatileHashSet<string>(new TimeSpan(0, 30, 0));

            _pushMulticastTagsRequest = new VolatileHashSet<Tag>(new TimeSpan(0, 30, 0));
            _pullMulticastTagsRequest = new VolatileHashSet<Tag>(new TimeSpan(0, 30, 0));
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

        public VolatileHashSet<byte[]> StockWikiDocumentMetadatas
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _stockWikiDocumentMetadatas;
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

        public VolatileHashSet<Key> PushBlocksLink
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksLink;
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

        public VolatileHashSet<Key> PushBlocksRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBlocksRequest;
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

        public VolatileHashSet<string> PushBroadcastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushBroadcastSignaturesRequest;
                }
            }
        }

        public VolatileHashSet<string> PullBroadcastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullBroadcastSignaturesRequest;
                }
            }
        }

        public VolatileHashSet<string> PushUnicastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushUnicastSignaturesRequest;
                }
            }
        }

        public VolatileHashSet<string> PullUnicastSignaturesRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullUnicastSignaturesRequest;
                }
            }
        }

        public VolatileHashSet<Tag> PushMulticastTagsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pushMulticastTagsRequest;
                }
            }
        }

        public VolatileHashSet<Tag> PullMulticastTagsRequest
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _pullMulticastTagsRequest;
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
