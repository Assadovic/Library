using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    class BackgroundDownloadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private WatchTimer _watchTimer;

        private Thread _downloadThread;
        private List<Thread> _decodeThreads = new List<Thread>();

        private VolatileHashSet<string> _lockSignatures;
        private VolatileHashSet<Tag> _lockTags;

        private Dictionary<Metadata, BackgroundDownloadItem> _downloadItems = new Dictionary<Metadata, BackgroundDownloadItem>();
        private Dictionary<Metadata, object> _cache_Results;

        private string _workDirectory = Path.GetTempPath();
        private ExistManager _existManager = new ExistManager();

        private ManagerState _state = ManagerState.Stop;

        private Thread _setThread;
        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();

        private Thread _removeThread;
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        private int _threadCount = 2;

        public BackgroundDownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 0, 30));

            _lockSignatures = new VolatileHashSet<string>(new TimeSpan(0, 10, 0));
            _lockTags = new VolatileHashSet<Tag>(new TimeSpan(0, 10, 0));

            _cache_Results = new Dictionary<Metadata, object>();

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);

            _cacheManager.BlockSetEvent += (IEnumerable<Key> keys) =>
            {
                foreach (var key in keys)
                {
                    _setKeys.Enqueue(key);
                }
            };

            _cacheManager.BlockRemoveEvent += (IEnumerable<Key> keys) =>
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
            _setThread.Name = "DownloadManager_SetThread";
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
            _removeThread.Name = "DownloadManager_RemoveThread";
            _removeThread.Start();

            _connectionsManager.GetLockSignaturesEvent = () =>
            {
                var signatures = new HashSet<string>();
                signatures.UnionWith(_settings.TrustSignatures);
                signatures.UnionWith(_lockSignatures);

                return signatures;
            };

            _connectionsManager.GetLockTagsEvent = () =>
            {
                var tags = new HashSet<Tag>();
                tags.UnionWith(_lockTags);

                return tags;
            };
        }

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.TrustSignatures.ToArray();
                }
            }
        }

        public void SetTrustSignatures(IEnumerable<string> signatures)
        {
            lock (_thisLock)
            {
                lock (_settings.TrustSignatures.ThisLock)
                {
                    _settings.TrustSignatures.Clear();
                    _settings.TrustSignatures.UnionWith(new SignatureCollection(signatures));
                }
            }
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
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
                    return text;
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

        private static string GetNormalizedPath(string path)
        {
            string filePath = path;

            foreach (char ic in Path.GetInvalidFileNameChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }
            foreach (char ic in Path.GetInvalidPathChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }

            return filePath;
        }

        private void WatchTimer()
        {
            try
            {
                if (this.State == ManagerState.Stop) return;

                {
                    _lockSignatures.TrimExcess();
                    _lockTags.TrimExcess();
                }

                lock (_thisLock)
                {
                    {
                        var now = DateTime.UtcNow;

                        foreach (var pair in _downloadItems.ToArray())
                        {
                            if ((now - pair.Value.UpdateTime).TotalMinutes > 10)
                            {
                                this.Remove(pair.Key);
                            }
                        }
                    }

                    foreach (var metadata in _cache_Results.Keys.ToArray())
                    {
                        if (_downloadItems.ContainsKey(metadata)) continue;
                        _cache_Results.Remove(metadata);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void Remove(Metadata metadata)
        {
            lock (_thisLock)
            {
                BackgroundDownloadItem item;
                if (!_downloadItems.TryGetValue(metadata, out item)) return;

                if (item.State != BackgroundDownloadState.Completed)
                {
                    _cacheManager.Unlock(metadata.Key);

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

                    this.UncheckState(item.Index);
                }

                if (item.Stream != null)
                {
                    item.Stream.Dispose();
                }

                _downloadItems.Remove(metadata);
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

        private void DownloadThread()
        {
            var random = new Random();
            int round = 0;

            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                Metadata metadata = null;
                BackgroundDownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_downloadItems.Count > 0)
                        {
                            {
                                var pairs = _downloadItems
                                   .Where(n => n.Value.State == BackgroundDownloadState.Downloading)
                                   .Where(x =>
                                   {
                                       if (x.Value.Depth == 1) return 0 == (!_cacheManager.Contains(x.Key.Key) ? 1 : 0);
                                       else return 0 == (x.Value.Index.Groups.Sum(n => n.InformationLength) - x.Value.Index.Groups.Sum(n => Math.Min(n.InformationLength, _existManager.GetCount(n))));
                                   })
                                   .ToList();

                                var pair = pairs.FirstOrDefault();
                                metadata = pair.Key;
                                item = pair.Value;
                            }

                            if (item == null)
                            {
                                var pairs = _downloadItems
                                    .Where(n => n.Value.State == BackgroundDownloadState.Downloading)
                                    .ToList();

                                if (pairs.Count > 0)
                                {
                                    round = (round >= pairs.Count) ? 0 : round;
                                    var pair = pairs[round++];
                                    metadata = pair.Key;
                                    item = pair.Value;
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
                        if (!_cacheManager.Contains(metadata.Key))
                        {
                            item.State = BackgroundDownloadState.Downloading;

                            _connectionsManager.Download(metadata.Key);
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

                            var limitCount = 256;

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
                }
            }
        }

        LockedHashSet<Metadata> _workingMetadatas = new LockedHashSet<Metadata>();

        private void DecodeThread()
        {
            var random = new Random();

            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                Metadata metadata = null;
                BackgroundDownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_downloadItems.Count > 0)
                        {
                            var pair = _downloadItems
                                .Where(n => n.Value.State == BackgroundDownloadState.Decoding)
                                .Where(n => !_workingMetadatas.Contains(n.Key))
                                .FirstOrDefault();

                            metadata = pair.Key;
                            item = pair.Value;

                            if (metadata != null)
                            {
                                _workingMetadatas.Add(metadata);
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
                    {
                        if ((item.Depth == 1 && !_cacheManager.Contains(metadata.Key))
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
                                keys.Add(metadata.Key);
                                compressionAlgorithm = metadata.CompressionAlgorithm;
                                cryptoAlgorithm = metadata.CryptoAlgorithm;
                                cryptoKey = metadata.CryptoKey;
                            }
                            else
                            {
                                item.State = BackgroundDownloadState.Decoding;

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        using (var tokenSource = new CancellationTokenSource())
                                        {
                                            var task = _cacheManager.ParityDecoding(group, tokenSource.Token);

                                            while (!task.IsCompleted)
                                            {
                                                if (this.State == ManagerState.Stop || !_downloadItems.ContainsKey(metadata)) tokenSource.Cancel();

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

                            if (item.Depth < metadata.Depth)
                            {
                                string fileName = null;
                                bool largeFlag = false;

                                try
                                {
                                    using (FileStream stream = BackgroundDownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_downloadItems.ContainsKey(metadata));

                                        if (!isStop && (stream.Length > 1024 * 1024 * 256))
                                        {
                                            isStop = true;
                                            largeFlag = true;
                                        }
                                    }, 1024 * 1024, true))
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
                                        throw new Exception("size too large.");
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

                                Stream stream = null;
                                bool largeFlag = false;

                                try
                                {
                                    stream = new BufferStream(_bufferManager);

                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.State == ManagerState.Stop || !_downloadItems.ContainsKey(metadata));

                                        if (!isStop && (stream.Length > 1024 * 1024 * 32))
                                        {
                                            isStop = true;
                                            largeFlag = true;
                                        }
                                    }, 1024 * 1024, true))
                                    {
                                        _cacheManager.Decoding(decodingProgressStream, compressionAlgorithm, cryptoAlgorithm, cryptoKey, keys);
                                    }
                                }
                                catch (StopIoException)
                                {
                                    if (stream != null) stream.Dispose();

                                    if (largeFlag)
                                    {
                                        throw new Exception("size too large.");
                                    }

                                    continue;
                                }
                                catch (Exception)
                                {
                                    if (stream != null) stream.Dispose();

                                    throw;
                                }

                                stream.Seek(0, SeekOrigin.Begin);

                                lock (_thisLock)
                                {
                                    this.UncheckState(item.Index);

                                    item.Index = null;

                                    item.Stream = stream;

                                    _cacheManager.Unlock(metadata.Key);

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
                }
                catch (Exception e)
                {
                    // Check
                    {
                        var list = new List<Key>();
                        list.Add(metadata.Key);

                        foreach (var index in item.Indexes)
                        {
                            foreach (var group in index.Groups)
                            {
                                foreach (var key in group.Keys)
                                {
                                    list.Add(key);
                                }
                            }
                        }

                        foreach (var key in list)
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

                    item.State = BackgroundDownloadState.Error;

                    Log.Error(e);
                }
                finally
                {
                    _workingMetadatas.Remove(metadata);
                }
            }
        }

        public T GetBroadcastContent<T>(string signature)
            where T : ItemBase<T>
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));

            lock (_thisLock)
            {
                _lockSignatures.Add(signature);

                var broadcastMetadata = _connectionsManager.GetBroadcastMetadatas(signature, typeof(T).Name);
                if (broadcastMetadata == null) return null;

                BackgroundDownloadItem item;

                if (!_downloadItems.TryGetValue(broadcastMetadata.Metadata, out item))
                {
                    item = new BackgroundDownloadItem();
                    item.Depth = 1;
                    item.State = BackgroundDownloadState.Downloading;

                    _cacheManager.Lock(broadcastMetadata.Metadata.Key);

                    _downloadItems.Add(broadcastMetadata.Metadata, item);
                }

                item.UpdateTime = DateTime.UtcNow;

                object result;

                if (!_cache_Results.TryGetValue(broadcastMetadata.Metadata, out result))
                {
                    if (item.State == BackgroundDownloadState.Completed)
                    {
                        result = ContentConverter.FromStream<T>(item.Stream);
                        _cache_Results[broadcastMetadata.Metadata] = result;

                        item.Stream.Dispose();
                        item.Stream = null;
                    }
                }

                return result as T;
            }
        }

        public Link GetLink(string signature)
        {
            return this.GetBroadcastContent<Link>(signature);
        }

        public Profile GetProfile(string signature)
        {
            return this.GetBroadcastContent<Profile>(signature);
        }

        public Store GetStore(string signature)
        {
            return this.GetBroadcastContent<Store>(signature);
        }

        public IEnumerable<Information> GetUnicastContents<T>(string signature, ExchangePrivateKey exchangePrivateKey)
            where T : ItemBase<T>
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (exchangePrivateKey == null) throw new ArgumentNullException(nameof(exchangePrivateKey));

            lock (_thisLock)
            {
                _lockSignatures.Add(signature);

                var informationList = new List<Information>();

                foreach (var unicastMetadata in _connectionsManager.GetUnicastMetadatas(signature, typeof(T).Name))
                {
                    if (!_settings.TrustSignatures.Contains(unicastMetadata.Certificate.ToString())) continue;

                    BackgroundDownloadItem item;

                    if (!_downloadItems.TryGetValue(unicastMetadata.Metadata, out item))
                    {
                        item = new BackgroundDownloadItem();
                        item.Depth = 1;
                        item.State = BackgroundDownloadState.Downloading;

                        _cacheManager.Lock(unicastMetadata.Metadata.Key);

                        _downloadItems.Add(unicastMetadata.Metadata, item);
                    }

                    item.UpdateTime = DateTime.UtcNow;

                    object result = null;

                    if (!_cache_Results.TryGetValue(unicastMetadata.Metadata, out result))
                    {
                        if (item.State == BackgroundDownloadState.Completed)
                        {
                            result = ContentConverter.FromCryptoStream<T>(item.Stream, exchangePrivateKey);
                            _cache_Results[unicastMetadata.Metadata] = result;

                            item.Stream.Dispose();
                            item.Stream = null;
                        }
                    }

                    if (result != null)
                    {
                        var contexts = new List<InformationContext>();
                        contexts.Add(new InformationContext("TargetSignature", signature));
                        contexts.Add(new InformationContext("CreationTime", unicastMetadata.CreationTime));
                        contexts.Add(new InformationContext("Signature", unicastMetadata.Certificate.ToString()));
                        contexts.Add(new InformationContext("Value", result));

                        informationList.Add(new Information(contexts));
                    }
                }

                return informationList;
            }
        }

        public IEnumerable<Information> GetUnicastMessages(string signature, ExchangePrivateKey exchangePrivateKey)
        {
            return this.GetUnicastContents<Message>(signature, exchangePrivateKey);
        }

        public IEnumerable<Information> GetMulticastContents<T>(Tag tag, int limit)
            where T : ItemBase<T>
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            lock (_thisLock)
            {
                _lockTags.Add(tag);

                var informationList = new List<Information>();

                foreach (var multicastMetadata in _connectionsManager.GetMulticastMetadatas(tag, typeof(T).Name))
                {
                    if (limit < 0)
                    {
                        if (!_settings.TrustSignatures.Contains(multicastMetadata.Certificate.ToString())) continue;
                    }
                    else
                    {
                        if (!_settings.TrustSignatures.Contains(multicastMetadata.Certificate.ToString()) && multicastMetadata.Cost < limit) continue;
                    }

                    BackgroundDownloadItem item;

                    if (!_downloadItems.TryGetValue(multicastMetadata.Metadata, out item))
                    {
                        item = new BackgroundDownloadItem();
                        item.Depth = 1;
                        item.State = BackgroundDownloadState.Downloading;

                        _cacheManager.Lock(multicastMetadata.Metadata.Key);

                        _downloadItems.Add(multicastMetadata.Metadata, item);
                    }

                    item.UpdateTime = DateTime.UtcNow;

                    object result = null;

                    if (!_cache_Results.TryGetValue(multicastMetadata.Metadata, out result))
                    {
                        if (item.State == BackgroundDownloadState.Completed)
                        {
                            result = ContentConverter.FromStream<T>(item.Stream);
                            _cache_Results[multicastMetadata.Metadata] = result;

                            item.Stream.Dispose();
                            item.Stream = null;
                        }
                    }

                    if (result != null)
                    {
                        var contexts = new List<InformationContext>();
                        contexts.Add(new InformationContext("Tag", tag));
                        contexts.Add(new InformationContext("CreationTime", multicastMetadata.CreationTime));
                        contexts.Add(new InformationContext("Signature", multicastMetadata.Certificate.ToString()));
                        contexts.Add(new InformationContext("Cost", multicastMetadata.Cost));
                        contexts.Add(new InformationContext("Value", result));

                        informationList.Add(new Information(contexts));
                    }
                }

                return informationList;
            }
        }

        public IEnumerable<Information> GetMulticastMessages(Tag tag, int limit)
        {
            return this.GetMulticastContents<Message>(tag, limit);
        }

        public IEnumerable<Information> GetMulticastWebsites(Tag tag, int limit)
        {
            return this.GetMulticastContents<Website>(tag, limit);
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

                    _downloadThread = new Thread(this.DownloadThread);
                    _downloadThread.Priority = ThreadPriority.BelowNormal;
                    _downloadThread.Name = "BackgroundDownloadManager_DownloadThread";
                    _downloadThread.Start();

                    for (int i = 0; i < _threadCount; i++)
                    {
                        var thread = new Thread(this.DecodeThread);
                        thread.Priority = ThreadPriority.BelowNormal;
                        thread.Name = "BackgroundDownloadManager_DecodeThread";
                        thread.Start();

                        _decodeThreads.Add(thread);
                    }
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

                _downloadThread.Join();
                _downloadThread = null;

                {
                    foreach (var thread in _decodeThreads)
                    {
                        thread.Join();
                    }

                    _decodeThreads.Clear();
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);
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
                    new Library.Configuration.SettingContent<LockedHashSet<string>>() { Name = "TrustSignatures", Value = new LockedHashSet<string>() },
                })
            {

            }

            public LockedHashSet<string> TrustSignatures
            {
                get
                {
                    return (LockedHashSet<string>)this["TrustSignatures"];
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_watchTimer != null)
                {
                    try
                    {
                        _watchTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _watchTimer = null;
                }

                _setKeys.Dispose();
                _removeKeys.Dispose();

                _setThread.Join();
                _removeThread.Join();
            }
        }
    }
}
