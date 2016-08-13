using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Outopos
{
    class UploadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _uploadThread;

        private WatchTimer _watchTimer;

        private ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha256;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(_thisLock);

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                lock (_thisLock)
                {
                    var list = new List<Information>();

                    foreach (var item in _settings.UploadItems.ToArray())
                    {
                        var contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Type", item.Type));

                        if (item.Type == "BroadcastMessage")
                        {
                            contexts.Add(new InformationContext("Message", item.BroadcastMessage));
                        }
                        else if (item.Type == "UnicastMessage")
                        {
                            contexts.Add(new InformationContext("Message", item.UnicastMessage));
                        }
                        else if (item.Type == "MulticastMessage")
                        {
                            contexts.Add(new InformationContext("Message", item.MulticastMessage));
                        }

                        contexts.Add(new InformationContext("DigitalSignature", item.DigitalSignature));

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        private void WatchTimer()
        {
            lock (_thisLock)
            {
                if (this.State == ManagerState.Stop) return;

                var now = DateTime.UtcNow;

                foreach (var item in _settings.LifeSpans.ToArray())
                {
                    if ((now - item.Value) > new TimeSpan(64, 0, 0, 0))
                    {
                        _cacheManager.Unlock(item.Key);
                        _settings.LifeSpans.Remove(item.Key);
                    }
                }
            }
        }

        private void UploadThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                {
                    UploadItem item = null;

                    lock (_thisLock)
                    {
                        if (_settings.UploadItems.Count > 0)
                        {
                            item = _settings.UploadItems[0];
                        }
                    }

                    try
                    {
                        if (item != null)
                        {
                            var buffer = default(ArraySegment<byte>);

                            try
                            {
                                if (item.Type == "BroadcastMessage")
                                {
                                    buffer = ContentConverter.ToBroadcastMessageBlock(item.BroadcastMessage);
                                }
                                else if (item.Type == "UnicastMessage")
                                {
                                    buffer = ContentConverter.ToUnicastMessageBlock(item.UnicastMessage, item.ExchangePublicKey);
                                }
                                else if (item.Type == "MulticastMessage")
                                {
                                    buffer = ContentConverter.ToMulticastMessageBlock(item.MulticastMessage);
                                }

                                Key key = null;

                                {
                                    if (_hashAlgorithm == HashAlgorithm.Sha256)
                                    {
                                        key = new Key(Sha256.ComputeHash(buffer), _hashAlgorithm);
                                    }

                                    this.Lock(key);
                                }

                                _cacheManager[key] = buffer;
                                _connectionsManager.Upload(key);

                                var miner = new Miner(CashAlgorithm.Version1, item.MiningLimit, item.MiningTime);

                                var task = Task.Run(() =>
                                {
                                    if (item.Type == "BroadcastMessage")
                                    {
                                        var metadata = new BroadcastMetadata(item.BroadcastMessage.CreationTime, key, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "UnicastMessage")
                                    {
                                        var metadata = new UnicastMetadata(item.UnicastMessage.Signature, item.UnicastMessage.CreationTime, key, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                    else if (item.Type == "MulticastMessage")
                                    {
                                        var metadata = new MulticastMetadata(item.MulticastMessage.Tag, item.MulticastMessage.CreationTime, key, miner, item.DigitalSignature);
                                        _connectionsManager.Upload(metadata);
                                    }
                                });

                                while (!task.IsCompleted)
                                {
                                    if (this.State == ManagerState.Stop) miner.Cancel();

                                    lock (_thisLock)
                                    {
                                        if (!_settings.UploadItems.Contains(item))
                                        {
                                            miner.Cancel();
                                        }
                                    }

                                    Thread.Sleep(1000);
                                }

                                if (task.Exception != null) throw task.Exception;

                                lock (_thisLock)
                                {
                                    _settings.UploadItems.Remove(item);
                                }
                            }
                            finally
                            {
                                if (buffer.Array != null)
                                {
                                    _bufferManager.ReturnBuffer(buffer.Array);
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }
        }

        private void Lock(Key key)
        {
            lock (_thisLock)
            {
                if (!_settings.LifeSpans.ContainsKey(key))
                {
                    _cacheManager.Lock(key);
                }

                _settings.LifeSpans[key] = DateTime.UtcNow;
            }
        }

        public BroadcastMessage UploadBroadcastMessage(
            int cost,
            ExchangePublicKey exchangePublicKey,
            IEnumerable<string> trustSignatures,
            IEnumerable<string> deleteSignatures,
            IEnumerable<Tag> tags,

            DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "BroadcastMessage";
                uploadItem.BroadcastMessage = new BroadcastMessage(DateTime.UtcNow, cost, exchangePublicKey, trustSignatures, deleteSignatures, tags, digitalSignature);
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Type == uploadItem.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.BroadcastMessage;
            }
        }

        public UnicastMessage UploadUnicastMessage(string signature,
            string comment,

            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "UnicastMessage";
                uploadItem.UnicastMessage = new UnicastMessage(signature, DateTime.UtcNow, comment, digitalSignature);
                uploadItem.ExchangePublicKey = exchangePublicKey;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.UnicastMessage;
            }
        }

        public MulticastMessage UploadMulticastMessage(Tag tag,
            string comment,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                var uploadItem = new UploadItem();
                uploadItem.Type = "MulticastMessage";
                uploadItem.MulticastMessage = new MulticastMessage(tag, DateTime.UtcNow, comment, digitalSignature);
                uploadItem.MiningLimit = miningLimit;
                uploadItem.MiningTime = miningTime;
                uploadItem.DigitalSignature = digitalSignature;

                _settings.UploadItems.Add(uploadItem);

                return uploadItem.MulticastMessage;
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (_thisLock)
                {
                    return _state;
                }
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
                lock (_thisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _watchTimer.Change(0, 1000 * 60 * 10);

                    _uploadThread = new Thread(this.UploadThread);
                    _uploadThread.Priority = ThreadPriority.Lowest;
                    _uploadThread.Name = "UploadManager_UploadManagerThread";
                    _uploadThread.Start();
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
            {
                lock (_thisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }

                _watchTimer.Change(Timeout.Infinite);

                _uploadThread.Join();
                _uploadThread = null;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);

                foreach (var key in _settings.LifeSpans.Keys)
                {
                    _cacheManager.Lock(key);
                }
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
                    new Library.Configuration.SettingContent<List<UploadItem>>() { Name = "UploadItems", Value = new List<UploadItem>() },
                    new Library.Configuration.SettingContent<Dictionary<Key, DateTime>>() { Name = "LifeSpans", Value = new Dictionary<Key, DateTime>() },
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

            public List<UploadItem> UploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (List<UploadItem>)this["UploadItems"];
                    }
                }
            }

            public Dictionary<Key, DateTime> LifeSpans
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (Dictionary<Key, DateTime>)this["LifeSpans"];
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
}
