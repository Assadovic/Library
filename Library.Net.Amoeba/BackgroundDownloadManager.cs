using System;
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
    class BackgroundDownloadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _downloadManagerThread;
        private Thread _decodeManagerThread;
        private Thread _watchThread;
        private string _workDirectory = Path.GetTempPath();
        private ExistManager _existManager = new ExistManager();

        private volatile ManagerState _state = ManagerState.Stop;

        private Thread _setThread;
        private Thread _removeThread;

        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public BackgroundDownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(_thisLock);

            _cacheManager.SetKeyEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _setKeys.Enqueue(key);
                }
            };

            _cacheManager.RemoveKeyEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _removeKeys.Enqueue(key);
                }
            };

            _setThread = new Thread(() =>
            {
                try
                {
                    for (;;)
                    {
                        var key = _setKeys.Dequeue();

                        lock (_thisLock)
                        {
                            _existManager.Set(key, true);
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _setThread.Priority = ThreadPriority.BelowNormal;
            _setThread.Name = "BackgroundDownloadManager_SetThread";
            _setThread.Start();

            _removeThread = new Thread(() =>
            {
                try
                {
                    for (;;)
                    {
                        var key = _removeKeys.Dequeue();

                        lock (_thisLock)
                        {
                            _existManager.Set(key, false);
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _removeThread.Priority = ThreadPriority.BelowNormal;
            _removeThread.Name = "BackgroundDownloadManager_RemoveThread";
            _removeThread.Start();

            _connectionsManager.GetLockSignaturesEvent = () =>
            {
                return this.SearchSignatures;
            };
        }

        public IEnumerable<string> SearchSignatures
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.Signatures.ToArray();
                }
            }
        }

        public void SetSearchSignatures(IEnumerable<string> signatures)
        {
            lock (_thisLock)
            {
                lock (_settings.Signatures.ThisLock)
                {
                    _settings.Signatures.Clear();
                    _settings.Signatures.UnionWith(new SignatureCollection(signatures));
                }
            }
        }

        private void CheckState(Index index)
        {
            lock (_thisLock)
            {
                if (index == null) return;

                foreach (var group in index.Groups)
                {
                    _existManager.Add(group);

                    {
                        var keys = new List<Key>();

                        foreach (var key in group.Keys)
                        {
                            if (!_cacheManager.Contains(key)) continue;
                            keys.Add(key);
                        }

                        _existManager.Set(group, keys);
                    }
                }
            }
        }

        private void UncheckState(Index index)
        {
            lock (_thisLock)
            {
                if (index == null) return;

                foreach (var group in index.Groups)
                {
                    _existManager.Remove(group);
                }
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    return new FileStream(path, FileMode.CreateNew);
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {

                }
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        return new FileStream(text, FileMode.CreateNew);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 1024) throw;
                    }
                }
            }
        }

        private void DownloadManagerThread()
        {
            var random = new Random();
            int round = 0;

            for (;;)
            {
                Thread.Sleep(1000 * 3);
                if (this.State == ManagerState.Stop) return;

                IBackgroundDownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.DownloadItems.GetItems().Any())
                        {
                            {
                                var items = _settings.DownloadItems.GetItems()
                                    .Where(n => n.State == BackgroundDownloadState.Downloading)
                                    .Where(x =>
                                    {
                                        if (x.Depth == 1) return 0 == (!_cacheManager.Contains(x.Seed.Metadata.Key) ? 1 : 0);
                                        else return 0 == (x.Index.Groups.Sum(n => n.InformationLength) - x.Index.Groups.Sum(n => Math.Min(n.InformationLength, _existManager.GetCount(n))));
                                    })
                                    .ToList();

                                item = items.FirstOrDefault();
                            }

                            if (item == null)
                            {
                                var items = _settings.DownloadItems.GetItems()
                                    .Where(n => n.State == BackgroundDownloadState.Downloading)
                                    .ToList();

                                if (items.Count > 0)
                                {
                                    round = (round >= items.Count) ? 0 : round;
                                    item = items[round++];
                                }
                            }
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
                    if (item.Depth == 1)
                    {
                        if (!_cacheManager.Contains(item.Seed.Metadata.Key))
                        {
                            item.State = BackgroundDownloadState.Downloading;

                            _connectionsManager.Download(item.Seed.Metadata.Key);
                        }
                        else
                        {
                            item.State = BackgroundDownloadState.Decoding;
                        }
                    }
                    else
                    {
                        if (!item.Index.Groups.All(n => _existManager.GetCount(n) >= n.InformationLength))
                        {
                            item.State = BackgroundDownloadState.Downloading;

                            int limitCount = 256;

                            foreach (var group in item.Index.Groups.ToArray().Randomize())
                            {
                                if (_existManager.GetCount(group) >= group.InformationLength) continue;

                                foreach (var key in _existManager.GetKeys(group, false))
                                {
                                    if (_connectionsManager.IsDownloadWaiting(key))
                                    {
                                        limitCount--;

                                        if (limitCount <= 0) goto End;
                                    }
                                }
                            }

                            foreach (var group in item.Index.Groups.ToArray().Randomize())
                            {
                                if (_existManager.GetCount(group) >= group.InformationLength) continue;

                                var tempKeys = new List<Key>();

                                foreach (var key in _existManager.GetKeys(group, false))
                                {
                                    if (!_connectionsManager.IsDownloadWaiting(key))
                                    {
                                        tempKeys.Add(key);
                                    }
                                }

                                random.Shuffle(tempKeys);
                                foreach (var key in tempKeys)
                                {
                                    _connectionsManager.Download(key);

                                    limitCount--;
                                }

                                if (limitCount <= 0) goto End;
                            }

                            End:;
                        }
                        else
                        {
                            item.State = BackgroundDownloadState.Decoding;
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = BackgroundDownloadState.Error;

                    Log.Error(e);

                    this.Remove(item);

                    Log.Error(string.Format("{0}: {1}", item.Value.GetType().Name, item.Seed.Certificate.ToString()));
                }
            }
        }

        private void DecodeManagerThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 3);
                if (this.State == ManagerState.Stop) return;

                IBackgroundDownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.DownloadItems.GetItems().Any())
                        {
                            item = _settings.DownloadItems.GetItems()
                                .Where(n => n.State == BackgroundDownloadState.Decoding)
                                .OrderBy(n => (n.Depth != n.Seed.Metadata.Depth) ? 0 : 1)
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
                    if ((item.Depth == 1 && !_cacheManager.Contains(item.Seed.Metadata.Key))
                        || (item.Depth > 1 && !item.Index.Groups.All(n => _existManager.GetCount(n) >= n.InformationLength)))
                    {
                        item.State = BackgroundDownloadState.Downloading;
                    }
                    else
                    {
                        var keys = new KeyCollection();
                        var compressionAlgorithm = CompressionAlgorithm.None;
                        var cryptoAlgorithm = CryptoAlgorithm.None;
                        byte[] cryptoKey = null;

                        if (item.Depth == 1)
                        {
                            keys.Add(item.Seed.Metadata.Key);
                            compressionAlgorithm = item.Seed.Metadata.CompressionAlgorithm;
                            cryptoAlgorithm = item.Seed.Metadata.CryptoAlgorithm;
                            cryptoKey = item.Seed.Metadata.CryptoKey;
                        }
                        else
                        {
                            try
                            {
                                foreach (var group in item.Index.Groups.ToArray())
                                {
                                    using (var tokenSource = new CancellationTokenSource())
                                    {
                                        var task = _cacheManager.ParityDecoding(group, tokenSource.Token);

                                        while (!task.IsCompleted)
                                        {
                                            if (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item)) tokenSource.Cancel();

                                            Thread.Sleep(1000);
                                        }

                                        keys.AddRange(task.Result);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            compressionAlgorithm = item.Index.CompressionAlgorithm;
                            cryptoAlgorithm = item.Index.CryptoAlgorithm;
                            cryptoKey = item.Index.CryptoKey;
                        }

                        item.State = BackgroundDownloadState.Decoding;

                        if (item.Depth < item.Seed.Metadata.Depth)
                        {
                            string fileName = null;
                            bool largeFlag = false;

                            try
                            {
                                using (FileStream stream = BackgroundDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                    if (!isStop && (stream.Length > item.Seed.Length))
                                    {
                                        isStop = true;
                                        largeFlag = true;
                                    }
                                }, 1024 * 1024 * 32, true))
                                {
                                    fileName = stream.Name;

                                    _cacheManager.Decoding(decodingProgressStream, compressionAlgorithm, cryptoAlgorithm, cryptoKey, keys);
                                }
                            }
                            catch (StopIoException)
                            {
                                if (File.Exists(fileName))
                                    File.Delete(fileName);

                                if (largeFlag)
                                {
                                    throw new Exception();
                                }

                                continue;
                            }
                            catch (Exception)
                            {
                                if (File.Exists(fileName))
                                    File.Delete(fileName);

                                throw;
                            }

                            Index index;

                            using (FileStream stream = new FileStream(fileName, FileMode.Open))
                            {
                                index = Index.Import(stream, _bufferManager);
                            }

                            File.Delete(fileName);

                            lock (_thisLock)
                            {
                                this.UncheckState(item.Index);

                                item.Index = index;

                                this.CheckState(item.Index);

                                foreach (var group in item.Index.Groups)
                                {
                                    foreach (var key in group.Keys)
                                    {
                                        _cacheManager.Lock(key);
                                    }
                                }

                                item.Indexes.Add(index);

                                item.Depth++;

                                item.State = BackgroundDownloadState.Downloading;
                            }
                        }
                        else
                        {
                            item.State = BackgroundDownloadState.Decoding;

                            bool largeFlag = false;
                            object value = null;

                            try
                            {
                                using (Stream stream = new BufferStream(_bufferManager))
                                using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.State == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                    if (!isStop && (stream.Length > item.Seed.Length))
                                    {
                                        isStop = true;
                                        largeFlag = true;
                                    }
                                }, 1024 * 1024 * 32, true))
                                {
                                    _cacheManager.Decoding(decodingProgressStream, compressionAlgorithm, cryptoAlgorithm, cryptoKey, keys);

                                    if (stream.Length != item.Seed.Length) throw new Exception();

                                    stream.Seek(0, SeekOrigin.Begin);

                                    if (item.Type == BackgroundItemType.Link)
                                    {
                                        value = Link.Import(stream, _bufferManager);
                                    }
                                    else if (item.Type == BackgroundItemType.Store)
                                    {
                                        value = Store.Import(stream, _bufferManager);
                                    }
                                }
                            }
                            catch (StopIoException)
                            {
                                if (largeFlag)
                                {
                                    throw new Exception();
                                }

                                continue;
                            }
                            catch (Exception)
                            {
                                throw;
                            }

                            lock (_thisLock)
                            {
                                item.Value = value;

                                _cacheManager.Unlock(item.Seed.Metadata.Key);

                                foreach (var index in item.Indexes)
                                {
                                    foreach (var group in index.Groups)
                                    {
                                        foreach (var key in group.Keys)
                                        {
                                            _cacheManager.Unlock(key);
                                        }
                                    }
                                }

                                item.Indexes.Clear();

                                item.State = BackgroundDownloadState.Completed;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    this.Check(item);

                    item.State = BackgroundDownloadState.Error;

                    Log.Error(e);
                }
            }
        }

        public void Check(IBackgroundDownloadItem item)
        {
            if (_cacheManager.Contains(item.Seed.Metadata.Key))
            {
                var buffer = new ArraySegment<byte>();

                try
                {
                    buffer = _cacheManager[item.Seed.Metadata.Key];
                }
                catch (Exception)
                {

                }
                finally
                {
                    if (buffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }

            foreach (var index in item.Indexes)
            {
                foreach (var group in index.Groups)
                {
                    foreach (var key in group.Keys)
                    {
                        if (this.State == ManagerState.Stop) return;

                        if (!_cacheManager.Contains(key)) continue;

                        var buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[key];
                        }
                        catch (Exception)
                        {

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
                            foreach (var item in _settings.DownloadItems.GetItems().ToArray())
                            {
                                if (item.State != BackgroundDownloadState.Error) continue;

                                this.Remove(item);
                            }

                            foreach (var item in _settings.DownloadItems.GetItems().ToArray())
                            {
                                if (this.SearchSignatures.Contains(item.Seed.Certificate.ToString())) continue;

                                this.Remove(item);
                            }

                            foreach (var signature in this.SearchSignatures.ToArray())
                            {
                                _connectionsManager.SendSeedsRequest(signature);

                                // Link
                                {
                                    Seed linkSeed;

                                    if (null != (linkSeed = _connectionsManager.GetLinkSeed(signature))
                                        && linkSeed.Length < 1024 * 1024 * 32)
                                    {
                                        var item = _settings.DownloadItems.GetItems()
                                            .Where(n => n.Type == BackgroundItemType.Link)
                                            .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);

                                        if (item == null)
                                        {
                                            this.Download(linkSeed, null);
                                        }
                                        else if (linkSeed.CreationTime > item.Seed.CreationTime)
                                        {
                                            this.Remove(item);
                                            this.Download(linkSeed, item.Value);
                                        }
                                    }
                                }

                                // Store
                                {
                                    Seed storeSeed;

                                    if (null != (storeSeed = _connectionsManager.GetStoreSeed(signature))
                                        && storeSeed.Length < 1024 * 1024 * 32)
                                    {
                                        var item = _settings.DownloadItems.GetItems()
                                            .Where(n => n.Type == BackgroundItemType.Store)
                                            .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);

                                        if (item == null)
                                        {
                                            this.Download(storeSeed, null);
                                        }
                                        else if (storeSeed.CreationTime > item.Seed.CreationTime)
                                        {
                                            this.Remove(item);
                                            this.Download(storeSeed, item.Value);
                                        }
                                    }
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

        private void Remove(IBackgroundDownloadItem item)
        {
            lock (_thisLock)
            {
                if (item.State != BackgroundDownloadState.Completed)
                {
                    if (item.Seed.Metadata != null)
                    {
                        _cacheManager.Unlock(item.Seed.Metadata.Key);
                    }

                    foreach (var index in item.Indexes)
                    {
                        foreach (var group in index.Groups)
                        {
                            foreach (var key in group.Keys)
                            {
                                _cacheManager.Unlock(key);
                            }
                        }
                    }
                }

                this.UncheckState(item.Index);

                _settings.DownloadItems.Remove(item);
            }
        }

        private void Download(Seed seed, object value)
        {
            if (seed == null) return;

            lock (_thisLock)
            {
                if (_settings.DownloadItems.GetItems().Any(n => n.Seed == seed)) return;

                if (seed.Metadata == null)
                {
                    IBackgroundDownloadItem item = null;

                    if (seed.Name == ConnectionsManager.Keyword_Link)
                    {
                        item = new BackgroundDownloadItem<Link>();
                        item.Value = new Link();
                    }
                    else if (seed.Name == ConnectionsManager.Keyword_Store)
                    {
                        item = new BackgroundDownloadItem<Store>();
                        item.Value = new Store();
                    }
                    else
                    {
                        throw new FormatException();
                    }

                    item.Depth = 0;
                    item.Seed = seed;
                    item.State = BackgroundDownloadState.Completed;

                    _settings.DownloadItems.Add(item);
                }
                else
                {
                    if (seed.Metadata.Key == null) return;

                    IBackgroundDownloadItem item = null;

                    if (seed.Name == ConnectionsManager.Keyword_Link)
                    {
                        item = new BackgroundDownloadItem<Link>();
                    }
                    else if (seed.Name == ConnectionsManager.Keyword_Store)
                    {
                        item = new BackgroundDownloadItem<Store>();
                    }
                    else
                    {
                        throw new FormatException();
                    }

                    item.Depth = 1;
                    item.Seed = seed;
                    item.State = BackgroundDownloadState.Downloading;
                    item.Value = value;

                    _cacheManager.Lock(item.Seed.Metadata.Key);

                    _settings.DownloadItems.Add(item);
                }
            }
        }

        public Link GetLink(string signature)
        {
            lock (_thisLock)
            {
                var item = _settings.DownloadItems.GetItems()
                    .Where(n => n.Type == BackgroundItemType.Link)
                    .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return null;

                var link = item.Value as Link;
                if (link == null) return null;

                return link;
            }
        }

        public Store GetStore(string signature)
        {
            lock (_thisLock)
            {
                var item = _settings.DownloadItems.GetItems()
                    .Where(n => n.Type == BackgroundItemType.Store)
                    .FirstOrDefault(n => n.Seed.Certificate.ToString() == signature);
                if (item == null) return null;

                var store = item.Value as Store;
                if (store == null) return null;

                return store;
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

                    _downloadManagerThread = new Thread(this.DownloadManagerThread);
                    _downloadManagerThread.Priority = ThreadPriority.Lowest;
                    _downloadManagerThread.Name = "BackgroundDownloadManager_DownloadManagerThread";
                    _downloadManagerThread.Start();

                    _decodeManagerThread = new Thread(this.DecodeManagerThread);
                    _decodeManagerThread.Priority = ThreadPriority.Lowest;
                    _decodeManagerThread.Name = "BackgroundDownloadManager_DecodeManagerThread";
                    _decodeManagerThread.Start();

                    _watchThread = new Thread(this.WatchThread);
                    _watchThread.Priority = ThreadPriority.Lowest;
                    _watchThread.Name = "BackgroundDownloadManager_WatchThread";
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

                _downloadManagerThread.Join();
                _downloadManagerThread = null;

                _decodeManagerThread.Join();
                _decodeManagerThread = null;

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

                foreach (var item in _settings.DownloadItems.GetItems())
                {
                    if (item.State != BackgroundDownloadState.Completed)
                    {
                        if (item.Seed.Metadata != null)
                        {
                            _cacheManager.Lock(item.Seed.Metadata.Key);
                        }

                        foreach (var index in item.Indexes)
                        {
                            foreach (var group in index.Groups)
                            {
                                foreach (var key in group.Keys)
                                {
                                    _cacheManager.Lock(key);
                                }
                            }
                        }
                    }
                }

                foreach (var item in _settings.DownloadItems.GetItems().ToArray())
                {
                    try
                    {
                        this.CheckState(item.Index);
                    }
                    catch (Exception)
                    {
                        _settings.DownloadItems.Remove(item);
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
                    new Library.Configuration.SettingContent<LockedList<BackgroundDownloadItem<Link>>>() { Name = "LinkBackgroundDownloadItems", Value = new LockedList<BackgroundDownloadItem<Link>>() },
                    new Library.Configuration.SettingContent<LockedList<BackgroundDownloadItem<Store>>>() { Name = "StoreBackgroundDownloadItems", Value = new LockedList<BackgroundDownloadItem<Store>>() },
                    new Library.Configuration.SettingContent<LockedHashSet<string>>() { Name = "Signatures", Value = new LockedHashSet<string>() },
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

            private DownloadItemsManager _downloadItems;

            public DownloadItemsManager DownloadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        if (_downloadItems == null)
                        {
                            _downloadItems = new DownloadItemsManager(this);
                        }

                        return _downloadItems;
                    }
                }
            }

            private LockedList<BackgroundDownloadItem<Link>> LinkBackgroundDownloadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundDownloadItem<Link>>)this["LinkBackgroundDownloadItems"];
                    }
                }
            }

            private LockedList<BackgroundDownloadItem<Store>> StoreBackgroundDownloadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<BackgroundDownloadItem<Store>>)this["StoreBackgroundDownloadItems"];
                    }
                }
            }

            public LockedHashSet<string> Signatures
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<string>)this["Signatures"];
                    }
                }
            }

            public class DownloadItemsManager
            {
                private Settings _settings;

                internal DownloadItemsManager(Settings settings)
                {
                    _settings = settings;
                }

                public void Add(IBackgroundDownloadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) _settings.LinkBackgroundDownloadItems.Add((BackgroundDownloadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) _settings.StoreBackgroundDownloadItems.Add((BackgroundDownloadItem<Store>)item);
                }

                public bool Contains(IBackgroundDownloadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) return _settings.LinkBackgroundDownloadItems.Contains((BackgroundDownloadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) return _settings.StoreBackgroundDownloadItems.Contains((BackgroundDownloadItem<Store>)item);

                    return false;
                }

                public void Remove(IBackgroundDownloadItem item)
                {
                    if (item.Type == BackgroundItemType.Link) _settings.LinkBackgroundDownloadItems.Remove((BackgroundDownloadItem<Link>)item);
                    else if (item.Type == BackgroundItemType.Store) _settings.StoreBackgroundDownloadItems.Remove((BackgroundDownloadItem<Store>)item);
                }

                public IEnumerable<IBackgroundDownloadItem> GetItems()
                {
                    return CollectionUtils.Unite(
                        _settings.LinkBackgroundDownloadItems.Cast<IBackgroundDownloadItem>(),
                        _settings.StoreBackgroundDownloadItems.Cast<IBackgroundDownloadItem>()
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
                if (_existManager != null)
                {
                    try
                    {
                        _existManager.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _existManager = null;
                }

                _setKeys.Dispose();
                _removeKeys.Dispose();

                _setThread.Join();
                _removeThread.Join();
            }
        }
    }
}
