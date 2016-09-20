using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library;
using Library.Collections;
using Library.Io;

namespace Library.Net.Amoeba
{
    // データ構造が複雑で、一時停止や途中からの再開なども考えるとこうなった

    class DownloadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _downloadThread;
        private List<Thread> _decodeThreads = new List<Thread>();

        private string _workDirectory = Path.GetTempPath();
        private ExistManager _existManager = new ExistManager();

        private ObjectIdManager<DownloadItem> _idManager = new ObjectIdManager<DownloadItem>();

        private volatile ManagerState _state = ManagerState.Stop;
        private volatile ManagerState _decodeState = ManagerState.Stop;

        private Thread _setThread;
        private WaitQueue<Key> _setKeys = new WaitQueue<Key>();

        private Thread _removeThread;
        private WaitQueue<Key> _removeKeys = new WaitQueue<Key>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private int _threadCount = 2;

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings();

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
        }

        public Information Information
        {
            get
            {
                lock (_thisLock)
                {
                    var contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("DownloadingCount", _settings.DownloadItems
                        .Count(n => !(n.State == DownloadState.Completed || n.State == DownloadState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                lock (_thisLock)
                {
                    var list = new List<Information>();

                    foreach (var pair in _idManager)
                    {
                        var id = pair.Key;
                        var item = pair.Value;

                        var contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", id));
                        contexts.Add(new InformationContext("Priority", item.Priority));
                        contexts.Add(new InformationContext("Name", DownloadManager.GetNormalizedPath(item.Seed.Name ?? "")));
                        contexts.Add(new InformationContext("Length", item.Seed.Length));
                        contexts.Add(new InformationContext("CreationTime", item.Seed.CreationTime));
                        contexts.Add(new InformationContext("State", item.State));
                        contexts.Add(new InformationContext("Depth", item.Depth));
                        if (item.Path != null) contexts.Add(new InformationContext("Path", Path.Combine(item.Path, DownloadManager.GetNormalizedPath(item.Seed.Name ?? ""))));
                        else contexts.Add(new InformationContext("Path", DownloadManager.GetNormalizedPath(item.Seed.Name ?? "")));

                        contexts.Add(new InformationContext("Seed", item.Seed));

                        if (item.State == DownloadState.Downloading || item.State == DownloadState.Completed || item.State == DownloadState.Error)
                        {
                            if (item.State == DownloadState.Downloading)
                            {
                                if (item.Depth == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Seed.Metadata.Key) ? 1 : 0));
                                else contexts.Add(new InformationContext("DownloadBlockCount", item.Index.Groups.Sum(n => Math.Min(n.InformationLength, _existManager.GetCount(n)))));
                            }
                            else if (item.State == DownloadState.Completed || item.State == DownloadState.Error)
                            {
                                if (item.Depth == 1) contexts.Add(new InformationContext("DownloadBlockCount", _cacheManager.Contains(item.Seed.Metadata.Key) ? 1 : 0));
                                else contexts.Add(new InformationContext("DownloadBlockCount", item.Index.Groups.Sum(n => _existManager.GetCount(n))));
                            }

                            if (item.Depth == 1) contexts.Add(new InformationContext("ParityBlockCount", 0));
                            else contexts.Add(new InformationContext("ParityBlockCount", item.Index.Groups.Sum(n => n.Keys.Count - n.InformationLength)));

                            if (item.Depth == 1) contexts.Add(new InformationContext("BlockCount", 1));
                            else contexts.Add(new InformationContext("BlockCount", item.Index.Groups.Sum(n => n.Keys.Count)));
                        }
                        else if (item.State == DownloadState.Decoding || item.State == DownloadState.ParityDecoding)
                        {
                            contexts.Add(new InformationContext("DecodeOffset", item.DecodeOffset));
                            contexts.Add(new InformationContext("DecodeLength", item.DecodeLength));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public string BaseDirectory
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.BaseDirectory;
                }
            }
            set
            {
                lock (_thisLock)
                {
                    _settings.BaseDirectory = value;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.DownloadedSeeds;
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

                DownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.DownloadItems.Count > 0)
                        {
                            {
                                var items = _settings.DownloadItems
                                   .Where(n => n.State == DownloadState.Downloading)
                                   .Where(n => n.Priority != 0)
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
                                var items = _settings.DownloadItems
                                    .Where(n => n.State == DownloadState.Downloading)
                                    .Where(n => n.Priority != 0)
                                    .OrderBy(n => -n.Priority)
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
                            item.State = DownloadState.Downloading;

                            _connectionsManager.Download(item.Seed.Metadata.Key);
                        }
                        else
                        {
                            item.State = DownloadState.Decoding;
                        }
                    }
                    else
                    {
                        if (!item.Index.Groups.All(n => _existManager.GetCount(n) >= n.InformationLength))
                        {
                            item.State = DownloadState.Downloading;

                            var limitCount = (int)(256 * Math.Pow(item.Priority, 3));

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
                            item.State = DownloadState.ParityDecoding;
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = DownloadState.Error;

                    Log.Error(e);
                }
            }
        }

        LockedHashSet<Seed> _workingSeeds = new LockedHashSet<Seed>();

        private void DecodeThread()
        {
            var random = new Random();

            for (;;)
            {
                Thread.Sleep(1000 * 3);
                if (this.DecodeState == ManagerState.Stop) return;

                DownloadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.DownloadItems.Count > 0)
                        {
                            item = _settings.DownloadItems
                                .Where(n => n.State == DownloadState.Decoding || n.State == DownloadState.ParityDecoding)
                                .Where(n => n.Priority != 0)
                                .OrderBy(n => (n.Depth != n.Seed.Metadata.Depth) ? 0 : 1)
                                .OrderBy(n => (n.State == DownloadState.Decoding) ? 0 : 1)
                                .Where(n => !_workingSeeds.Contains(n.Seed))
                                .FirstOrDefault();

                            if (item != null)
                            {
                                _workingSeeds.Add(item.Seed);
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
                        if ((item.Depth == 1 && !_cacheManager.Contains(item.Seed.Metadata.Key))
                            || (item.Depth > 1 && !item.Index.Groups.All(n => _existManager.GetCount(n) >= n.InformationLength)))
                        {
                            item.State = DownloadState.Downloading;
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
                                item.State = DownloadState.ParityDecoding;

                                item.DecodeOffset = 0;
                                item.DecodeLength = item.Index.Groups.Sum(n => n.Length);

                                try
                                {
                                    foreach (var group in item.Index.Groups.ToArray())
                                    {
                                        using (var tokenSource = new CancellationTokenSource())
                                        {
                                            var task = _cacheManager.ParityDecoding(group, tokenSource.Token);

                                            while (!task.IsCompleted)
                                            {
                                                if (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item)) tokenSource.Cancel();

                                                Thread.Sleep(1000);
                                            }

                                            keys.AddRange(task.Result);
                                        }

                                        item.DecodeOffset += group.Length;
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

                            item.State = DownloadState.Decoding;

                            if (item.Depth < item.Seed.Metadata.Depth)
                            {
                                string fileName = null;
                                bool largeFlag = false;

                                try
                                {
                                    item.DecodeOffset = 0;
                                    item.DecodeLength = keys.Sum(n => _cacheManager.GetLength(n));

                                    using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(_workDirectory, "index")))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                        if (!isStop && (stream.Length > item.Seed.Length))
                                        {
                                            isStop = true;
                                            largeFlag = true;
                                        }

                                        item.DecodeOffset = writeSize;
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
                                    item.DecodeOffset = 0;
                                    item.DecodeLength = 0;

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

                                    item.State = DownloadState.Downloading;
                                }
                            }
                            else
                            {
                                item.State = DownloadState.Decoding;

                                string fileName = null;
                                bool largeFlag = false;
                                string downloadDirectory;

                                if (item.Path == null)
                                {
                                    downloadDirectory = this.BaseDirectory;
                                }
                                else
                                {
                                    if (Path.IsPathRooted(item.Path))
                                    {
                                        downloadDirectory = item.Path;
                                    }
                                    else
                                    {
                                        downloadDirectory = Path.Combine(this.BaseDirectory, item.Path);
                                    }
                                }

                                Directory.CreateDirectory(downloadDirectory);

                                try
                                {
                                    item.DecodeOffset = 0;
                                    item.DecodeLength = keys.Sum(n => _cacheManager.GetLength(n));

                                    using (FileStream stream = DownloadManager.GetUniqueFileStream(Path.Combine(downloadDirectory, string.Format("{0}.tmp", DownloadManager.GetNormalizedPath(item.Seed.Name)))))
                                    using (ProgressStream decodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                    {
                                        isStop = (this.DecodeState == ManagerState.Stop || !_settings.DownloadItems.Contains(item));

                                        if (!isStop && (stream.Length > item.Seed.Length))
                                        {
                                            isStop = true;
                                            largeFlag = true;
                                        }

                                        item.DecodeOffset = writeSize;
                                    }, 1024 * 1024, true))
                                    {
                                        fileName = stream.Name;

                                        _cacheManager.Decoding(decodingProgressStream, compressionAlgorithm, cryptoAlgorithm, cryptoKey, keys);

                                        if (stream.Length != item.Seed.Length) throw new Exception("Stream.Length != Seed.Length");
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

                                File.Move(fileName, DownloadManager.GetUniqueFilePath(Path.Combine(downloadDirectory, DownloadManager.GetNormalizedPath(item.Seed.Name))));

                                lock (_thisLock)
                                {
                                    item.DecodeOffset = 0;
                                    item.DecodeLength = 0;

                                    {
                                        var usingKeys = new HashSet<Key>();

                                        foreach (var index in item.Indexes)
                                        {
                                            foreach (var group in index.Groups)
                                            {
                                                usingKeys.UnionWith(group.Keys
                                                    .Where(n => _cacheManager.Contains(n))
                                                    .Reverse()
                                                    .Take(group.InformationLength));
                                            }
                                        }

                                        _cacheManager.SetSeed(item.Seed.Clone(), usingKeys.ToArray());
                                    }

                                    _settings.DownloadedSeeds.Add(item.Seed.Clone());

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

                                    item.State = DownloadState.Completed;
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
                        list.Add(item.Seed.Metadata.Key);

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
                            if (this.DecodeState == ManagerState.Stop) return;
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

                    item.State = DownloadState.Error;

                    Log.Error(e);
                }
                finally
                {
                    _workingSeeds.Remove(item.Seed);
                }
            }
        }

        public void Download(Seed seed,
            int priority)
        {
            lock (_thisLock)
            {
                this.Download(seed, null, priority);
            }
        }

        public void Download(Seed seed,
            string path,
            int priority)
        {
            if (seed == null) return;

            lock (_thisLock)
            {
                if (_settings.DownloadItems.Any(n => n.Seed == seed && n.Path == path)) return;

                {
                    if (seed.Metadata == null) return;
                    if (seed.Metadata.Key == null) return;

                    var item = new DownloadItem();

                    item.Depth = 1;
                    item.Seed = seed;
                    item.Path = path;
                    item.State = DownloadState.Downloading;
                    item.Priority = priority;

                    _cacheManager.Lock(item.Seed.Metadata.Key);

                    _settings.DownloadItems.Add(item);
                    _idManager.Add(item);
                }
            }
        }

        public void Remove(int id)
        {
            lock (_thisLock)
            {
                var item = _idManager.GetItem(id);

                if (item.State != DownloadState.Completed)
                {
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
                }

                this.UncheckState(item.Index);

                _settings.DownloadItems.Remove(item);
                _idManager.Remove(id);
            }
        }

        public void Reset(int id)
        {
            lock (_thisLock)
            {
                var item = _idManager.GetItem(id);

                this.Remove(id);
                this.Download(item.Seed, item.Path, item.Priority);
            }
        }

        public void SetPriority(int id, int priority)
        {
            lock (_thisLock)
            {
                var item = _idManager.GetItem(id);

                item.Priority = priority;
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        public ManagerState DecodeState
        {
            get
            {
                return _decodeState;
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
                    _downloadThread.Name = "DownloadManager_DownloadThread";
                    _downloadThread.Start();
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
            }
        }

        private readonly object _decodeStateLock = new object();

        public void DecodeStart()
        {
            lock (_decodeStateLock)
            {
                lock (_thisLock)
                {
                    if (this.DecodeState == ManagerState.Start) return;
                    _decodeState = ManagerState.Start;

                    for (int i = 0; i < _threadCount; i++)
                    {
                        var thread = new Thread(this.DecodeThread);
                        thread.Priority = ThreadPriority.BelowNormal;
                        thread.Name = "DownloadManager_DecodeThread";
                        thread.Start();

                        _decodeThreads.Add(thread);
                    }
                }
            }
        }

        public void DecodeStop()
        {
            lock (_decodeStateLock)
            {
                lock (_thisLock)
                {
                    if (this.DecodeState == ManagerState.Stop) return;
                    _decodeState = ManagerState.Stop;
                }

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

                foreach (var item in _settings.DownloadItems)
                {
                    if (item.State != DownloadState.Completed)
                    {
                        _cacheManager.Lock(item.Seed.Metadata.Key);

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

                foreach (var item in _settings.DownloadItems.ToArray())
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

                _idManager.Clear();

                foreach (var item in _settings.DownloadItems)
                {
                    _idManager.Add(item);
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
                    new Library.Configuration.SettingContent<string>() { Name = "BaseDirectory", Value = "" },
                    new Library.Configuration.SettingContent<LockedList<DownloadItem>>() { Name = "DownloadItems", Value = new LockedList<DownloadItem>() },
                    new Library.Configuration.SettingContent<SeedCollection>() { Name = "DownloadedSeeds", Value = new SeedCollection() },
                })
            {

            }

            public string BaseDirectory
            {
                get
                {
                    return (string)this["BaseDirectory"];
                }
                set
                {
                    this["BaseDirectory"] = value;
                }
            }

            public LockedList<DownloadItem> DownloadItems
            {
                get
                {
                    return (LockedList<DownloadItem>)this["DownloadItems"];
                }
            }

            public SeedCollection DownloadedSeeds
            {
                get
                {
                    return (SeedCollection)this["DownloadedSeeds"];
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
