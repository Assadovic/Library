using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    class BackgroundUploadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _encodeThread;

        private WatchTimer _watchTimer;

        private volatile ManagerState _state = ManagerState.Stop;

        private Thread _uploadedThread;
        private WaitQueue<Key> _uploadedKeys = new WaitQueue<Key>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public BackgroundUploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);

            _connectionsManager.BlockUploadedEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _cacheManager.BlockRemoveEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _uploadedThread = new Thread(() =>
            {
                try
                {
                    for (;;)
                    {
                        var key = _uploadedKeys.Dequeue();

                        lock (_thisLock)
                        {
                            foreach (var item in _settings.UploadItems.ToArray())
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    if (item.State == BackgroundUploadState.Uploading)
                                    {
                                        if (item.UploadKeys.Count == 0)
                                        {
                                            item.State = BackgroundUploadState.Completed;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _uploadedThread.Priority = ThreadPriority.BelowNormal;
            _uploadedThread.Name = "BackgroundUploadManager_UploadedThread";
            _uploadedThread.Start();
        }

        private void CheckState(BackgroundUploadItem item)
        {
            lock (_thisLock)
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.IsUploadWaiting(key))
                    {
                        item.UploadKeys.Remove(key);
                    }
                }

                if (item.State == BackgroundUploadState.Uploading)
                {
                    if (item.UploadKeys.Count == 0)
                    {
                        item.State = BackgroundUploadState.Completed;
                    }
                }
            }
        }

        private void WatchTimer()
        {
            try
            {
                if (this.State == ManagerState.Stop) return;

                lock (_thisLock)
                {
                    var now = DateTime.UtcNow;

                    foreach (var item in _settings.UploadItems.ToArray())
                    {
                        if (item.State == BackgroundUploadState.Completed
                            && (now - item.CreationTime).TotalDays > 32)
                        {
                            this.Remove(item);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private void Remove(BackgroundUploadItem item)
        {
            lock (_thisLock)
            {
                foreach (var key in item.LockedKeys)
                {
                    _cacheManager.Unlock(key);
                }

                _settings.UploadItems.Remove(item);
            }
        }

        private void EncodeThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                BackgroundUploadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.UploadItems.Count > 0)
                        {
                            item = _settings.UploadItems
                                .Where(n => n.State == BackgroundUploadState.Encoding)
                                .FirstOrDefault();
                        }
                    }
                }
                catch (Exception)
                {
                    return;
                }

                if (item == null) continue;

                try
                {
                    if (item.Groups.Count == 0 && item.Keys.Count == 0)
                    {
                        Stream stream = null;

                        try
                        {
                            if (item.Scheme == "Broadcast")
                            {
                                if (item.Type == "Link")
                                {
                                    var value = item.Link;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToLinkStream(value);
                                }
                                else if (item.Type == "Profile")
                                {
                                    var value = item.Profile;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToProfileStream(value);
                                }
                                else if (item.Type == "Store")
                                {
                                    var value = item.Store;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToStoreStream(value);
                                }
                            }
                            else if (item.Scheme == "Unicast")
                            {
                                if (item.Type == "Message")
                                {
                                    var value = item.Message;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToUnicastMessageStream(value, item.ExchangePublicKey);
                                }
                            }
                            else if (item.Scheme == "Multicast")
                            {
                                if (item.Type == "Message")
                                {
                                    var value = item.Message;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToMulticastMessageStream(value);
                                }
                                else if (item.Type == "Website")
                                {
                                    var value = item.Website;
                                    if (value == null) throw new FormatException();

                                    stream = ContentConverter.ToMulticastWebsiteStream(value);
                                }
                            }
                            else
                            {
                                throw new FormatException();
                            }

                            if (stream.Length == 0)
                            {
                                lock (_thisLock)
                                {
                                    if (item.Scheme == "Broadcast")
                                    {
                                        _connectionsManager.Upload(new BroadcastMetadata(item.Type, item.CreationTime, null, item.DigitalSignature));
                                    }
                                    else if (item.Scheme == "Unicast")
                                    {
                                        _connectionsManager.Upload(new UnicastMetadata(item.Type, item.Signature, item.CreationTime, null, item.DigitalSignature));
                                    }
                                    else if (item.Scheme == "Multicast")
                                    {
                                        _connectionsManager.Upload(new MulticastMetadata(item.Type, item.Tag, item.CreationTime, null, null, item.DigitalSignature));
                                    }

                                    item.State = BackgroundUploadState.Completed;
                                }
                            }
                            else
                            {
                                KeyCollection keys = null;

                                try
                                {
                                    using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                                    }, 1024 * 1024, true))
                                    {
                                        if (stream.Length == 0) throw new InvalidOperationException("Stream Length");

                                        encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                        item.State = BackgroundUploadState.Encoding;
                                        keys = _cacheManager.Encoding(encodingProgressStream, CompressionAlgorithm.None, CryptoAlgorithm.None, null, item.BlockLength, item.HashAlgorithm);
                                    }
                                }
                                catch (StopIoException)
                                {
                                    continue;
                                }

                                lock (_thisLock)
                                {
                                    foreach (var key in keys)
                                    {
                                        item.UploadKeys.Add(key);
                                        item.LockedKeys.Add(key);
                                    }

                                    item.Keys.AddRange(keys);
                                }
                            }
                        }
                        finally
                        {
                            if (stream != null) stream.Dispose();
                        }
                    }
                    else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                    {
                        BroadcastMetadata broadcastMetadata = null;
                        UnicastMetadata unicastMetadata = null;
                        MulticastMetadata multicastMetadata = null;

                        {
                            var metadata = new Metadata(item.Depth, item.Keys[0], CompressionAlgorithm.None, CryptoAlgorithm.None, null);

                            if (item.Scheme == "Broadcast")
                            {
                                broadcastMetadata = new BroadcastMetadata(item.Type, item.CreationTime, metadata, item.DigitalSignature);
                            }
                            else if (item.Scheme == "Unicast")
                            {
                                unicastMetadata = new UnicastMetadata(item.Type, item.Signature, item.CreationTime, metadata, item.DigitalSignature);
                            }
                            else if (item.Scheme == "Multicast")
                            {
                                var miner = new Miner(CashAlgorithm.Version1, item.MiningLimit, item.MiningTime);

                                var task = Task.Run(() =>
                                {
                                    multicastMetadata = new MulticastMetadata(item.Type, item.Tag, item.CreationTime, metadata, miner, item.DigitalSignature);
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
                            }
                        }

                        lock (_thisLock)
                        {
                            if (item.Scheme == "Broadcast")
                            {
                                _connectionsManager.Upload(broadcastMetadata);
                            }
                            else if (item.Scheme == "Unicast")
                            {
                                _connectionsManager.Upload(unicastMetadata);
                            }
                            else if (item.Scheme == "Multicast")
                            {
                                _connectionsManager.Upload(multicastMetadata);
                            }

                            item.Keys.Clear();

                            foreach (var key in item.UploadKeys)
                            {
                                _connectionsManager.Upload(key);
                            }

                            item.State = BackgroundUploadState.Uploading;

                            this.CheckState(item);
                        }
                    }
                    else if (item.Keys.Count > 0)
                    {
                        var length = Math.Min(item.Keys.Count, 128);
                        var keys = new KeyCollection(item.Keys.Take(length));
                        Group group = null;

                        try
                        {
                            using (var tokenSource = new CancellationTokenSource())
                            {
                                var task = _cacheManager.ParityEncoding(keys, item.HashAlgorithm, item.BlockLength, item.CorrectionAlgorithm, tokenSource.Token);

                                while (!task.IsCompleted)
                                {
                                    if (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item)) tokenSource.Cancel();

                                    Thread.Sleep(1000);
                                }

                                group = task.Result;
                            }
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        lock (_thisLock)
                        {
                            foreach (var key in group.Keys.Skip(group.InformationLength))
                            {
                                item.UploadKeys.Add(key);
                                item.LockedKeys.Add(key);
                            }

                            item.Groups.Add(group);

                            item.Keys.RemoveRange(0, length);
                        }
                    }
                    else if (item.Groups.Count > 0 && item.Keys.Count == 0)
                    {
                        var index = new Index();
                        index.Groups.AddRange(item.Groups);

                        KeyCollection keys = null;

                        try
                        {
                            using (var stream = index.Export(_bufferManager))
                            using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                            {
                                isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                            }, 1024 * 1024, true))
                            {
                                encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                item.State = BackgroundUploadState.Encoding;
                                keys = _cacheManager.Encoding(encodingProgressStream, CompressionAlgorithm.None, CryptoAlgorithm.None, null, item.BlockLength, item.HashAlgorithm);
                            }
                        }
                        catch (StopIoException)
                        {
                            continue;
                        }

                        lock (_thisLock)
                        {
                            foreach (var key in keys)
                            {
                                item.UploadKeys.Add(key);
                                item.LockedKeys.Add(key);
                            }

                            item.Keys.AddRange(keys);
                            item.Depth++;
                            item.Groups.Clear();
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = BackgroundUploadState.Error;

                    Log.Error(e);

                    this.Remove(item);
                }
            }
        }

        public void Upload(Link link, DigitalSignature digitalSignature)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Link = link;
                item.Scheme = "Broadcast";
                item.Type = "Link";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
        }

        public void Upload(Profile profile, DigitalSignature digitalSignature)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Profile = profile;
                item.Scheme = "Broadcast";
                item.Type = "Profile";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
        }

        public void Upload(Store store, DigitalSignature digitalSignature)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Store = store;
                item.Scheme = "Broadcast";
                item.Type = "Store";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
        }

        public void UnicastUpload(string signature,
            Message message,

            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (exchangePublicKey == null) throw new ArgumentNullException(nameof(exchangePublicKey));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Signature = signature;
                item.Message = message;
                item.Scheme = "Unicast";
                item.Type = "Message";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.ExchangePublicKey = exchangePublicKey;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
        }

        public void MulticastUpload(Tag tag,
            Message message,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Tag = tag;
                item.Message = message;
                item.Scheme = "Multicast";
                item.Type = "Message";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.MiningLimit = miningLimit;
                item.MiningTime = miningTime;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
        }

        public void MulticastUpload(Tag tag,
            Website website,

            DigitalSignature digitalSignature)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (website == null) throw new ArgumentNullException(nameof(website));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                var item = new BackgroundUploadItem();

                item.State = BackgroundUploadState.Encoding;
                item.Tag = tag;
                item.Website = website;
                item.Scheme = "Multicast";
                item.Type = "Website";
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.BlockLength = 1024 * 1024 * 1;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.MiningLimit = 0;
                item.MiningTime = TimeSpan.Zero;
                item.DigitalSignature = digitalSignature;

                _settings.UploadItems.RemoveAll((target) =>
                {
                    return target.Scheme == item.Scheme && target.Type == item.Type
                        && target.DigitalSignature == digitalSignature;
                });

                _settings.UploadItems.Add(item);
            }
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
            lock (_stateLock)
            {
                lock (_thisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _encodeThread = new Thread(this.EncodeThread);
                    _encodeThread.Priority = ThreadPriority.Lowest;
                    _encodeThread.Name = "BackgroundUploadManager_EndocdeThread";
                    _encodeThread.Start();

                    _watchTimer.Change(0, 1000 * 60 * 10);
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

                _encodeThread.Join();
                _encodeThread = null;

                _watchTimer.Change(Timeout.Infinite);
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.UploadItems)
                {
                    foreach (var key in item.LockedKeys)
                    {
                        _cacheManager.Lock(key);
                    }
                }

                foreach (var item in _settings.UploadItems.ToArray())
                {
                    try
                    {
                        this.CheckState(item);
                    }
                    catch (Exception)
                    {
                        _settings.UploadItems.Remove(item);
                    }
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
            public Settings()
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<LockedList<BackgroundUploadItem>>() { Name = "UploadItems", Value = new LockedList<BackgroundUploadItem>() },
                })
            {

            }

            public LockedList<BackgroundUploadItem> UploadItems
            {
                get
                {
                    return (LockedList<BackgroundUploadItem>)this["UploadItems"];
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _uploadedKeys.Dispose();

                _uploadedThread.Join();
            }
        }
    }
}
