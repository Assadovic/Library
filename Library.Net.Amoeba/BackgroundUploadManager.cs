﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    class BackgroundUploadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _uploadManagerThread;
        private Thread _watchThread;

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

            _settings = new Settings(_thisLock);

            _connectionsManager.UploadedEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _uploadedKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveKeyEvent += (IEnumerable<Key> keys) =>
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
                            foreach (var item in _settings.UploadItems.GetItems().ToArray())
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    item.UploadedKeys.Add(key);

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

        private void SetKeyCount(IBackgroundUploadItem item)
        {
            lock (_thisLock)
            {
                foreach (var key in item.UploadKeys.ToArray())
                {
                    if (!_connectionsManager.IsUploadWaiting(key))
                    {
                        item.UploadedKeys.Add(key);
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

        private void UploadManagerThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                IBackgroundUploadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.UploadItems.GetItems().Any())
                        {
                            item = _settings.UploadItems.GetItems()
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
                            if (item.Type == BackgroundItemType.Link)
                            {
                                var link = item.Value as Link;
                                if (link == null) throw new FormatException();

                                stream = link.Export(_bufferManager);
                            }
                            else if (item.Type == BackgroundItemType.Store)
                            {
                                var store = item.Value as Store;
                                if (store == null) throw new FormatException();

                                stream = store.Export(_bufferManager);
                            }
                            else
                            {
                                throw new FormatException();
                            }

                            if (stream.Length == 0)
                            {
                                lock (_thisLock)
                                {
                                    item.Seed = new Seed(null);
                                    item.Seed.Name = item.Name;
                                    item.Seed.Length = item.Length;
                                    item.CreationTime = item.CreationTime;

                                    if (item.DigitalSignature != null)
                                    {
                                        item.Seed.CreateCertificate(item.DigitalSignature);
                                    }

                                    _connectionsManager.Upload(item.Seed);

                                    item.State = BackgroundUploadState.Completed;
                                }
                            }
                            else
                            {
                                KeyCollection keys = null;
                                byte[] cryptoKey = null;

                                try
                                {
                                    using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                                    }, 1024 * 1024 * 32, true))
                                    {
                                        if (stream.Length == 0) throw new InvalidOperationException("Stream Length");

                                        item.Length = stream.Length;

                                        if (item.HashAlgorithm == HashAlgorithm.Sha256)
                                        {
                                            cryptoKey = Sha256.ComputeHash(encodingProgressStream);
                                        }

                                        encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                        item.State = BackgroundUploadState.Encoding;
                                        keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.BlockLength, item.HashAlgorithm);
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

                                    item.CryptoKey = cryptoKey;
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
                        lock (_thisLock)
                        {
                            var metadata = new Metadata(item.Depth, item.Keys[0], item.CompressionAlgorithm, item.CryptoAlgorithm, item.CryptoKey);

                            item.Keys.Clear();

                            item.Seed = new Seed(metadata);
                            item.Seed.Name = item.Name;
                            item.Seed.Length = item.Length;
                            item.Seed.CreationTime = item.CreationTime;

                            if (item.DigitalSignature != null)
                            {
                                item.Seed.CreateCertificate(item.DigitalSignature);
                            }

                            item.UploadKeys.Add(item.Seed.Metadata.Key);

                            foreach (var key in item.UploadKeys)
                            {
                                _connectionsManager.Upload(key);
                            }

                            this.SetKeyCount(item);

                            foreach (var key in item.LockedKeys)
                            {
                                _cacheManager.Unlock(key);
                            }

                            item.LockedKeys.Clear();

                            item.State = BackgroundUploadState.Uploading;

                            _connectionsManager.Upload(item.Seed);
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
                        index.CompressionAlgorithm = item.CompressionAlgorithm;
                        index.CryptoAlgorithm = item.CryptoAlgorithm;
                        index.CryptoKey = item.CryptoKey;

                        byte[] cryptoKey = null;
                        KeyCollection keys = null;

                        try
                        {
                            using (var stream = index.Export(_bufferManager))
                            using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                            {
                                isStop = (this.State == ManagerState.Stop || !_settings.UploadItems.Contains(item));
                            }, 1024 * 1024 * 32, true))
                            {
                                if (item.HashAlgorithm == HashAlgorithm.Sha256)
                                {
                                    cryptoKey = Sha256.ComputeHash(encodingProgressStream);
                                }

                                encodingProgressStream.Seek(0, SeekOrigin.Begin);

                                item.State = BackgroundUploadState.Encoding;
                                keys = _cacheManager.Encoding(encodingProgressStream, item.CompressionAlgorithm, item.CryptoAlgorithm, cryptoKey, item.BlockLength, item.HashAlgorithm);
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

                            item.CryptoKey = cryptoKey;
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

        private void WatchThread()
        {
            var watchStopwatch = new Stopwatch();

            try
            {
                for (;;)
                {
                    Thread.Sleep(1000);
                    if (this.State == ManagerState.Stop) return;

                    if (!watchStopwatch.IsRunning || watchStopwatch.Elapsed.TotalSeconds >= 60)
                    {
                        watchStopwatch.Restart();

                        lock (_thisLock)
                        {
                            var now = DateTime.UtcNow;

                            foreach (var item in _settings.UploadItems.GetItems().ToArray())
                            {
                                if (item.State == BackgroundUploadState.Completed
                                    && (now - item.Seed.CreationTime) > new TimeSpan(32, 0, 0, 0))
                                {
                                    this.Remove(item);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void Upload(Link link, DigitalSignature digitalSignature)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                {
                    foreach (var item in _settings.UploadItems.GetItems().ToArray())
                    {
                        if (item.DigitalSignature.ToString() != digitalSignature.ToString()) continue;

                        this.Remove(item);
                    }
                }

                {
                    var item = new BackgroundUploadItem<Link>();

                    item.Value = link;
                    item.State = BackgroundUploadState.Encoding;
                    item.Name = ConnectionsManager.Keyword_Link;
                    item.CreationTime = DateTime.UtcNow;
                    item.Depth = 1;
                    item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                    item.CryptoAlgorithm = CryptoAlgorithm.Aes256;
                    item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                    item.HashAlgorithm = HashAlgorithm.Sha256;
                    item.DigitalSignature = digitalSignature;
                    item.BlockLength = 1024 * 1024 * 1;

                    _settings.UploadItems.Add(item);
                }
            }
        }

        public void Upload(Store store, DigitalSignature digitalSignature)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            lock (_thisLock)
            {
                {
                    foreach (var item in _settings.UploadItems.GetItems().ToArray())
                    {
                        if (item.DigitalSignature.ToString() != digitalSignature.ToString()) continue;

                        this.Remove(item);
                    }
                }

                {
                    var item = new BackgroundUploadItem<Store>();

                    item.Value = store;
                    item.State = BackgroundUploadState.Encoding;
                    item.Name = ConnectionsManager.Keyword_Store;
                    item.CreationTime = DateTime.UtcNow;
                    item.Depth = 1;
                    item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                    item.CryptoAlgorithm = CryptoAlgorithm.Aes256;
                    item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                    item.HashAlgorithm = HashAlgorithm.Sha256;
                    item.DigitalSignature = digitalSignature;
                    item.BlockLength = 1024 * 1024 * 1;

                    _settings.UploadItems.Add(item);
                }
            }
        }

        private void Remove(IBackgroundUploadItem item)
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

                    _uploadManagerThread = new Thread(this.UploadManagerThread);
                    _uploadManagerThread.Priority = ThreadPriority.Lowest;
                    _uploadManagerThread.Name = "BackgroundUploadManager_UploadManagerThread";
                    _uploadManagerThread.Start();

                    _watchThread = new Thread(this.WatchThread);
                    _watchThread.Priority = ThreadPriority.Lowest;
                    _watchThread.Name = "BackgroundUploadManager_WatchThread";
                    _watchThread.Start();
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

                _uploadManagerThread.Join();
                _uploadManagerThread = null;

                _watchThread.Join();
                _watchThread = null;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);

                foreach (var item in _settings.UploadItems.GetItems())
                {
                    foreach (var key in item.LockedKeys)
                    {
                        _cacheManager.Lock(key);
                    }
                }

                foreach (var item in _settings.UploadItems.GetItems().ToArray())
                {
                    try
                    {
                        this.SetKeyCount(item);
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
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<LockedList<BackgroundUploadItem<Link>>>() { Name = "LinkBackgroundUploadItems", Value = new LockedList<BackgroundUploadItem<Link>>() },
                    new Library.Configuration.SettingContent<LockedList<BackgroundUploadItem<Store>>>() { Name = "StoreBackgroundUploadItems", Value = new LockedList<BackgroundUploadItem<Store>>() },
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

            private UploadItemsManager _downloadItems;

            public UploadItemsManager UploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        if (_downloadItems == null)
                        {
                            _downloadItems = new UploadItemsManager(this);
                        }

                        return _downloadItems;
                    }
                }
            }

            private LockedList<BackgroundUploadItem<Link>> LinkBackgroundUploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundUploadItem<Link>>)this["LinkBackgroundUploadItems"];
                    }
                }
            }

            private LockedList<BackgroundUploadItem<Store>> StoreBackgroundUploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundUploadItem<Store>>)this["StoreBackgroundUploadItems"];
                    }
                }
            }

            public class UploadItemsManager
            {
                private Settings _settings;

                internal UploadItemsManager(Settings settings)
                {
                    _settings = settings;
                }

                public void Add(IBackgroundUploadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) _settings.LinkBackgroundUploadItems.Add((BackgroundUploadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) _settings.StoreBackgroundUploadItems.Add((BackgroundUploadItem<Store>)item);
                }

                public bool Contains(IBackgroundUploadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) return _settings.LinkBackgroundUploadItems.Contains((BackgroundUploadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) return _settings.StoreBackgroundUploadItems.Contains((BackgroundUploadItem<Store>)item);

                    return false;
                }

                public void Remove(IBackgroundUploadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) _settings.LinkBackgroundUploadItems.Remove((BackgroundUploadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) _settings.StoreBackgroundUploadItems.Remove((BackgroundUploadItem<Store>)item);
                }

                public IEnumerable<IBackgroundUploadItem> GetItems()
                {
                    return CollectionUtils.Unite(
                        _settings.LinkBackgroundUploadItems.Cast<IBackgroundUploadItem>(),
                        _settings.StoreBackgroundUploadItems.Cast<IBackgroundUploadItem>()
                    );
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
