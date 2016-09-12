using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    // 全体的にカオスだけど、進行状況の報告とか考えるとこんな風になってしまった

    class UploadManager : StateManagerBase, Library.Configuration.ISettings
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private List<Thread> _encodeThreads = new List<Thread>();

        private ObjectIdManager<UploadItem> _idManager = new ObjectIdManager<UploadItem>();
        private Dictionary<string, int> _shareIdLink = new Dictionary<string, int>();

        private volatile ManagerState _state = ManagerState.Stop;
        private volatile ManagerState _encodeState = ManagerState.Stop;

        private Thread _uploadedThread;
        private WaitQueue<Key> _uploadedKeys = new WaitQueue<Key>();

        private Thread _shareRemoveThread;
        private WaitQueue<string> _shareRemovePaths = new WaitQueue<string>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private int _threadCount = 2;

        public UploadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _settings = new Settings(_thisLock);

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);

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

            _cacheManager.ShareRemoveEvent += (string path) =>
            {
                _shareRemovePaths.Enqueue(path);
            };

            _uploadedThread = new Thread(() =>
            {
                try
                {
                    for (;;)
                    {
                        var key = _uploadedKeys.Dequeue();

                        while (_shareRemovePaths.Count > 0) Thread.Sleep(1000);

                        lock (_thisLock)
                        {
                            foreach (var item in _settings.UploadItems)
                            {
                                if (item.UploadKeys.Remove(key))
                                {
                                    item.UploadedKeys.Add(key);

                                    if (item.State == UploadState.Uploading)
                                    {
                                        if (item.UploadKeys.Count == 0)
                                        {
                                            item.State = UploadState.Completed;

                                            _settings.UploadedSeeds.Add(item.Seed.Clone());
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
            _uploadedThread.Name = "UploadManager_UploadedThread";
            _uploadedThread.Start();

            _shareRemoveThread = new Thread(() =>
            {
                try
                {
                    for (;;)
                    {
                        var path = _shareRemovePaths.Dequeue();

                        lock (_thisLock)
                        {
                            int id;

                            if (_shareIdLink.TryGetValue(path, out id))
                            {
                                this.Remove(id);
                            }
                        }
                    }
                }
                catch (Exception)
                {

                }
            });
            _shareRemoveThread.Priority = ThreadPriority.BelowNormal;
            _shareRemoveThread.Name = "UploadManager_ShareRemoveThread";
            _shareRemoveThread.Start();
        }

        public Information Information
        {
            get
            {
                lock (_thisLock)
                {
                    var contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("UploadingCount", _settings.UploadItems
                        .Count(n => !(n.State == UploadState.Completed || n.State == UploadState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
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
                        contexts.Add(new InformationContext("Name", item.Name));
                        contexts.Add(new InformationContext("Length", item.Length));
                        contexts.Add(new InformationContext("CreationTime", item.CreationTime));
                        contexts.Add(new InformationContext("State", item.State));
                        contexts.Add(new InformationContext("Depth", item.Depth));
                        contexts.Add(new InformationContext("Path", item.FilePath));

                        if (item.State == UploadState.Completed || item.State == UploadState.Uploading)
                        {
                            contexts.Add(new InformationContext("Seed", item.Seed));
                        }

                        if (item.State == UploadState.Uploading)
                        {
                            contexts.Add(new InformationContext("UploadBlockCount", item.UploadedKeys.Count));
                            contexts.Add(new InformationContext("BlockCount", item.UploadKeys.Count + item.UploadedKeys.Count));
                        }
                        else if (item.State == UploadState.Encoding || item.State == UploadState.ComputeHash || item.State == UploadState.ParityEncoding)
                        {
                            contexts.Add(new InformationContext("EncodeOffset", item.EncodeOffset));
                            contexts.Add(new InformationContext("EncodeLength", item.EncodeLength));
                        }
                        else if (item.State == UploadState.Completed)
                        {
                            contexts.Add(new InformationContext("UploadBlockCount", item.UploadedKeys.Count));
                            contexts.Add(new InformationContext("BlockCount", item.UploadKeys.Count + item.UploadedKeys.Count));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                lock (_thisLock)
                {
                    return _settings.UploadedSeeds;
                }
            }
        }

        private void CheckState(UploadItem item)
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

                if (item.State == UploadState.Uploading)
                {
                    if (item.UploadKeys.Count == 0)
                    {
                        item.State = UploadState.Completed;

                        _settings.UploadedSeeds.Add(item.Seed.Clone());
                    }
                }
            }
        }

        LockedHashSet<string> _workingPaths = new LockedHashSet<string>();

        private void EncodeThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.EncodeState == ManagerState.Stop) return;

                UploadItem item = null;

                try
                {
                    lock (_thisLock)
                    {
                        if (_settings.UploadItems.Count > 0)
                        {
                            item = _settings.UploadItems
                                .Where(n => n.State == UploadState.ComputeHash || n.State == UploadState.Encoding || n.State == UploadState.ParityEncoding)
                                .Where(n => n.Priority != 0)
                                .OrderBy(n => -n.Priority)
                                .Where(n => !_workingPaths.Contains(n.FilePath))
                                .FirstOrDefault();

                            if (item != null)
                            {
                                _workingPaths.Add(item.FilePath);
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
                    if (item.Groups.Count == 0 && item.Keys.Count == 0)
                    {
                        if (item.Type == UploadType.Upload)
                        {
                            item.State = UploadState.Encoding;

                            KeyCollection keys = null;
                            byte[] cryptoKey = null;

                            try
                            {
                                using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodeOffset = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodeOffset = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    if (stream.Length == 0) throw new InvalidOperationException("Stream Length");

                                    item.Length = stream.Length;
                                    item.EncodeLength = stream.Length;

                                    item.State = UploadState.ComputeHash;

                                    if (item.HashAlgorithm == HashAlgorithm.Sha256)
                                    {
                                        cryptoKey = Sha256.ComputeHash(hashProgressStream);
                                    }

                                    stream.Seek(0, SeekOrigin.Begin);
                                    item.EncodeOffset = 0;

                                    item.State = UploadState.Encoding;
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

                                item.EncodeOffset = 0;
                                item.EncodeLength = 0;

                                item.CryptoKey = cryptoKey;
                                item.Keys.AddRange(keys);
                            }
                        }
                        else if (item.Type == UploadType.Share)
                        {
                            item.State = UploadState.ComputeHash;

                            KeyCollection keys = null;

                            try
                            {
                                using (FileStream stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                                {
                                    isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                    item.EncodeOffset = Math.Min(readSize, stream.Length);
                                }, 1024 * 1024, true))
                                {
                                    if (stream.Length == 0) throw new InvalidOperationException("Stream Length");

                                    item.Length = stream.Length;
                                    item.EncodeLength = stream.Length;

                                    keys = _cacheManager.Share(hashProgressStream, stream.Name, item.HashAlgorithm, item.BlockLength);
                                }
                            }
                            catch (StopIoException)
                            {
                                continue;
                            }

                            if (keys.Count == 1)
                            {
                                lock (_thisLock)
                                {
                                    item.EncodeOffset = 0;
                                    item.EncodeLength = 0;

                                    item.Keys.Add(keys[0]);

                                    item.State = UploadState.Encoding;
                                }
                            }
                            else
                            {
                                var groups = new List<Group>();

                                for (int i = 0, remain = keys.Count; 0 < remain; i++, remain -= 256)
                                {
                                    var tempKeys = keys.GetRange(i * 256, Math.Min(remain, 256));

                                    Group group = null;

                                    try
                                    {
                                        using (var tokenSource = new CancellationTokenSource())
                                        {
                                            var task = _cacheManager.ParityEncoding(new KeyCollection(tempKeys), item.HashAlgorithm, item.BlockLength, CorrectionAlgorithm.None, tokenSource.Token);

                                            while (!task.IsCompleted)
                                            {
                                                if ((this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item))) tokenSource.Cancel();

                                                Thread.Sleep(1000);
                                            }

                                            group = task.Result;
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        break;
                                    }

                                    groups.Add(group);
                                }

                                if ((this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item))) continue;

                                lock (_thisLock)
                                {
                                    item.EncodeOffset = 0;
                                    item.EncodeLength = 0;

                                    foreach (var key in keys)
                                    {
                                        item.UploadKeys.Add(key);
                                    }

                                    item.Groups.AddRange(groups);

                                    item.State = UploadState.Encoding;
                                }
                            }
                        }
                    }
                    else if (item.Groups.Count == 0 && item.Keys.Count == 1)
                    {
                        lock (_thisLock)
                        {
                            Metadata metadata = null;
                            {
                                if (item.Type == UploadType.Upload)
                                {
                                    metadata = new Metadata(item.Depth, item.Keys[0], item.CompressionAlgorithm, item.CryptoAlgorithm, item.CryptoKey);
                                }
                                else if (item.Type == UploadType.Share)
                                {
                                    if (item.Depth == 1)
                                    {
                                        metadata = new Metadata(item.Depth, item.Keys[0], CompressionAlgorithm.None, CryptoAlgorithm.None, null);
                                    }
                                    else
                                    {
                                        metadata = new Metadata(item.Depth, item.Keys[0], item.CompressionAlgorithm, item.CryptoAlgorithm, item.CryptoKey);
                                    }
                                }

                                item.Keys.Clear();
                            }

                            item.Seed = new Seed(metadata);
                            item.Seed.Name = item.Name;
                            item.Seed.Length = item.Length;
                            item.Seed.CreationTime = item.CreationTime;
                            item.Seed.Keywords.AddRange(item.Keywords);

                            if (item.DigitalSignature != null)
                            {
                                item.Seed.CreateCertificate(item.DigitalSignature);
                            }

                            foreach (var key in item.UploadKeys)
                            {
                                _connectionsManager.Upload(key);
                            }

                            {
                                if (item.Type == UploadType.Upload)
                                {
                                    _cacheManager.SetSeed(item.Seed.Clone(), item.RetainKeys.ToArray());
                                }
                                else if (item.Type == UploadType.Share)
                                {
                                    _cacheManager.SetSeed(item.Seed.Clone(), item.FilePath, item.RetainKeys.ToArray());
                                }

                                item.RetainKeys.Clear();
                            }

                            foreach (var key in item.LockedKeys)
                            {
                                _cacheManager.Unlock(key);
                            }

                            item.LockedKeys.Clear();

                            item.State = UploadState.Uploading;

                            this.CheckState(item);
                        }
                    }
                    else if (item.Keys.Count > 0)
                    {
                        item.State = UploadState.ParityEncoding;

                        item.EncodeLength = item.Groups.Sum(n =>
                        {
                            long sumLength = 0;

                            for (int i = 0; i < n.InformationLength; i++)
                            {
                                if (_cacheManager.Contains(n.Keys[i]))
                                {
                                    sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                }
                            }

                            return sumLength;
                        }) + item.Keys.Sum(n =>
                        {
                            if (_cacheManager.Contains(n))
                            {
                                return (long)_cacheManager.GetLength(n);
                            }

                            return 0;
                        });

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
                                    if ((this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item))) tokenSource.Cancel();

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

                            foreach (var key in group.Keys.Skip(group.Keys.Count - group.InformationLength))
                            {
                                item.RetainKeys.Add(key);
                            }

                            item.Groups.Add(group);

                            item.EncodeOffset = item.Groups.Sum(n =>
                            {
                                long sumLength = 0;

                                for (int i = 0; i < n.InformationLength; i++)
                                {
                                    if (_cacheManager.Contains(n.Keys[i]))
                                    {
                                        sumLength += (long)_cacheManager.GetLength(n.Keys[i]);
                                    }
                                }

                                return sumLength;
                            });

                            item.Keys.RemoveRange(0, length);
                        }
                    }
                    else if (item.Groups.Count > 0 && item.Keys.Count == 0)
                    {
                        item.State = UploadState.Encoding;

                        var index = new Index();

                        if (item.Type == UploadType.Upload)
                        {
                            index.Groups.AddRange(item.Groups);

                            index.CompressionAlgorithm = item.CompressionAlgorithm;

                            index.CryptoAlgorithm = item.CryptoAlgorithm;
                            index.CryptoKey = item.CryptoKey;
                        }
                        else if (item.Type == UploadType.Share)
                        {
                            index.Groups.AddRange(item.Groups);

                            if (item.Depth != 1)
                            {
                                index.CompressionAlgorithm = item.CompressionAlgorithm;

                                index.CryptoAlgorithm = item.CryptoAlgorithm;
                                index.CryptoKey = item.CryptoKey;
                            }
                        }

                        byte[] cryptoKey = null;
                        KeyCollection keys = null;

                        try
                        {
                            using (var stream = index.Export(_bufferManager))
                            using (ProgressStream hashProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                            {
                                isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                item.EncodeOffset = Math.Min(readSize, stream.Length);
                            }, 1024 * 1024, true))
                            using (ProgressStream encodingProgressStream = new ProgressStream(stream, (object sender, long readSize, long writeSize, out bool isStop) =>
                            {
                                isStop = (this.EncodeState == ManagerState.Stop || !_settings.UploadItems.Contains(item));

                                item.EncodeOffset = Math.Min(readSize, stream.Length);
                            }, 1024 * 1024, true))
                            {
                                item.EncodeLength = stream.Length;

                                item.State = UploadState.ComputeHash;

                                if (item.HashAlgorithm == HashAlgorithm.Sha256)
                                {
                                    cryptoKey = Sha256.ComputeHash(hashProgressStream);
                                }

                                stream.Seek(0, SeekOrigin.Begin);
                                item.EncodeOffset = 0;

                                item.State = UploadState.Encoding;
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

                            item.EncodeOffset = 0;
                            item.EncodeLength = 0;

                            item.CryptoKey = cryptoKey;
                            item.Keys.AddRange(keys);
                            item.Depth++;
                            item.Groups.Clear();
                        }
                    }
                }
                catch (Exception e)
                {
                    item.State = UploadState.Error;

                    Log.Error(e);
                }
                finally
                {
                    _workingPaths.Remove(item.FilePath);
                }
            }
        }

        public void Upload(string filePath,
            string name,
            IEnumerable<string> keywords,
            DigitalSignature digitalSignature,
            int priority)
        {
            lock (_thisLock)
            {
                var item = new UploadItem();

                item.State = UploadState.ComputeHash;
                item.Type = UploadType.Upload;
                item.Name = name;
                item.Keywords.AddRange(keywords);
                item.CreationTime = DateTime.UtcNow;
                item.Depth = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                item.CryptoAlgorithm = CryptoAlgorithm.Aes256;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.DigitalSignature = digitalSignature;
                item.BlockLength = 1024 * 1024 * 1;
                item.Priority = priority;

                _settings.UploadItems.Add(item);
                _idManager.Add(item);
            }
        }

        public void Share(string filePath,
            string name,
            IEnumerable<string> keywords,
            DigitalSignature digitalSignature,
            int priority)
        {
            lock (_thisLock)
            {
                if (_settings.UploadItems
                    .Where(n => n.Type == UploadType.Share)
                    .Any(n => n.FilePath == filePath)) return;

                if (_cacheManager.Contains(filePath)) return;

                var item = new UploadItem();

                item.State = UploadState.ComputeHash;
                item.Type = UploadType.Share;
                item.Name = name;
                item.CreationTime = DateTime.UtcNow;
                item.Keywords.AddRange(keywords);
                item.Depth = 1;
                item.FilePath = filePath;
                item.CompressionAlgorithm = CompressionAlgorithm.Xz;
                item.CryptoAlgorithm = CryptoAlgorithm.Aes256;
                item.CorrectionAlgorithm = CorrectionAlgorithm.ReedSolomon8;
                item.HashAlgorithm = HashAlgorithm.Sha256;
                item.DigitalSignature = digitalSignature;
                item.BlockLength = 1024 * 1024 * 1;
                item.Priority = priority;

                _settings.UploadItems.Add(item);

                int id = _idManager.Add(item);
                _shareIdLink.Add(filePath, id);
            }
        }

        public void Remove(int id)
        {
            lock (_thisLock)
            {
                var item = _idManager.GetItem(id);

                foreach (var key in item.LockedKeys)
                {
                    _cacheManager.Unlock(key);
                }

                _settings.UploadItems.Remove(item);
                _idManager.Remove(id);

                if (item.Type == UploadType.Share)
                {
                    _shareIdLink.Remove(item.FilePath);
                }
            }
        }

        public void Reset(int id)
        {
            lock (_thisLock)
            {
                var item = _idManager.GetItem(id);

                this.Remove(id);

                if (item.Type == UploadType.Upload)
                {
                    this.Upload(item.FilePath,
                        item.Name,
                        item.Keywords,
                        item.DigitalSignature,
                        item.Priority);
                }
                else if (item.Type == UploadType.Share)
                {
                    this.Share(item.FilePath,
                        item.Name,
                        item.Keywords,
                        item.DigitalSignature,
                        item.Priority);
                }
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

        public ManagerState EncodeState
        {
            get
            {
                return _encodeState;
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
            }
        }

        private readonly object _encodeStateLock = new object();

        public void EncodeStart()
        {
            lock (_encodeStateLock)
            {
                lock (_thisLock)
                {
                    if (this.EncodeState == ManagerState.Start) return;
                    _encodeState = ManagerState.Start;

                    for (int i = 0; i < _threadCount; i++)
                    {
                        var thread = new Thread(this.EncodeThread);
                        thread.Priority = ThreadPriority.BelowNormal;
                        thread.Name = "UploadManager_EncodeThread";
                        thread.Start();

                        _encodeThreads.Add(thread);
                    }
                }
            }
        }

        public void EncodeStop()
        {
            lock (_encodeStateLock)
            {
                lock (_thisLock)
                {
                    if (this.EncodeState == ManagerState.Stop) return;
                    _encodeState = ManagerState.Stop;
                }

                {
                    foreach (var thread in _encodeThreads)
                    {
                        thread.Join();
                    }

                    _encodeThreads.Clear();
                }
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

                _idManager.Clear();
                _shareIdLink.Clear();

                foreach (var item in _settings.UploadItems)
                {
                    int id = _idManager.Add(item);

                    if (item.Type == UploadType.Share)
                    {
                        _shareIdLink.Add(item.FilePath, id);
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
                    new Library.Configuration.SettingContent<LockedList<UploadItem>>() { Name = "UploadItems", Value = new LockedList<UploadItem>() },
                    new Library.Configuration.SettingContent<SeedCollection>() { Name = "UploadedSeeds", Value = new SeedCollection() },
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

            public LockedList<UploadItem> UploadItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<UploadItem>)this["UploadItems"];
                    }
                }
            }

            public SeedCollection UploadedSeeds
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (SeedCollection)this["UploadedSeeds"];
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
                _uploadedKeys.Dispose();
                _shareRemovePaths.Dispose();

                _uploadedThread.Join();
                _shareRemoveThread.Join();
            }
        }
    }
}
