using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Compression;
using Library.Correction;
using Library.Messaging;
using Library.Security;
using Library.Utilities;

namespace Library.Net.Amoeba
{
    public delegate void CheckBlocksProgressEventHandler(object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop);

    interface ISetOperators<T>
    {
        IEnumerable<T> IntersectFrom(IEnumerable<T> collection);
        IEnumerable<T> ExceptFrom(IEnumerable<T> collection);
    }

    class CacheManager : ManagerBase, Library.Configuration.ISettings, ISetOperators<Key>, IEnumerable<Key>, IThisLock
    {
        private FileStream _fileStream;
        private BitmapManager _bitmapManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private bool _spaceSectors_Initialized;
        private SortedSet<long> _spaceSectors = new SortedSet<long>();

        private bool _shareIndexLink_Initialized;
        private Dictionary<Key, string> _shareIndexLink = new Dictionary<Key, string>();

        private long _lockSpace;
        private long _freeSpace;

        private Dictionary<Key, int> _lockedKeys = new Dictionary<Key, int>();

        private EventQueue<Key> _blockSetEventQueue = new EventQueue<Key>();
        private EventQueue<Key> _blockRemoveEventQueue = new EventQueue<Key>();
        private EventQueue<string> _shareRemoveEventQueue = new EventQueue<string>();

        private WatchTimer _watchTimer;
        private WatchTimer _checkTimer;

        private readonly object _convertLock = new object();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public static readonly int SectorSize = 1024 * 256;
        public static readonly int SpaceSectorUnit = 4 * 1024; // 1MB * 1024 = 1024MB

        private int _threadCount = 2;

        public CacheManager(string blocksPath, BitmapManager bitmapManager, BufferManager bufferManager)
        {
            const int FILE_FLAG_NO_BUFFERING = 0x20000000;

            _fileStream = new FileStream(blocksPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, CacheManager.SectorSize, (FileOptions)FILE_FLAG_NO_BUFFERING);
            _bitmapManager = bitmapManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);

            _watchTimer = new WatchTimer(this.WatchTimer, Timeout.Infinite);
            _checkTimer = new WatchTimer(this.CheckTimer, Timeout.Infinite);
        }

        private static long Roundup(long value, long unit)
        {
            if (value % unit == 0) return value;
            else return ((value / unit) + 1) * unit;
        }

        private void WatchTimer()
        {
            this.CheckInformation();
        }

        private void CheckTimer()
        {
            this.CheckSeeds();
        }

        private void CheckInformation()
        {
            lock (this.ThisLock)
            {
                try
                {
                    var usingKeys = new HashSet<Key>();
                    usingKeys.UnionWith(_lockedKeys.Keys);
                    usingKeys.UnionWith(_settings.SeedIndex.Keys.Select(n => n.Metadata.Key));
                    usingKeys.UnionWith(_settings.SeedIndex.Values.SelectMany(n => n.Keys));

                    long size = 0;

                    foreach (var key in usingKeys)
                    {
                        ClusterInfo clusterInfo;

                        if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                        {
                            size += clusterInfo.Indexes.Length * CacheManager.SectorSize;
                        }
                    }

                    _lockSpace = size;
                    _freeSpace = this.Size - size;
                }
                catch (Exception)
                {

                }
            }
        }

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.SeedIndex.Keys.ToArray();
                }
            }
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    var contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("SeedCount", _settings.SeedIndex.Count));
                    contexts.Add(new InformationContext("ShareCount", _settings.ShareIndex.Count));
                    contexts.Add(new InformationContext("UsingSpace", _fileStream.Length));
                    contexts.Add(new InformationContext("LockSpace", _lockSpace));
                    contexts.Add(new InformationContext("FreeSpace", _freeSpace));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    var list = new List<Information>();

                    foreach (var item in _settings.ShareIndex)
                    {
                        var contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Path", item.Key));
                        contexts.Add(new InformationContext("BlockCount", item.Value.Indexes.Count));

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public long Size
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Size;
                }
            }
        }

        public long Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    return (long)_settings.ClusterIndex.Count + _settings.ShareIndex.Sum(n => (long)n.Value.Indexes.Count);
                }
            }
        }

        private void CheckSeeds()
        {
            lock (this.ThisLock)
            {
#if DEBUG
                var sw = new Stopwatch();
                sw.Start();
#endif

                var pathList = new HashSet<string>();
                pathList.UnionWith(_settings.ShareIndex.Keys);

                foreach (var pair in _settings.SeedIndex.ToArray())
                {
                    var seed = pair.Key;
                    var info = pair.Value;

                    if (info.Path != null)
                    {
                        if (!pathList.Contains(info.Path)) goto End;
                    }

                    if (!this.Contains(seed.Metadata.Key)) goto End;

                    foreach (var key in info.Keys)
                    {
                        if (!this.Contains(key)) goto End;
                    }

                    continue;

                    End:;

                    {
                        if (info.Path != null)
                        {
                            _settings.ShareIndex.Remove(info.Path);
                        }

                        _settings.SeedIndex.Remove(seed);
                    }
                }

#if DEBUG
                sw.Stop();
                Debug.WriteLine("CheckSeeds {0}", sw.ElapsedMilliseconds);
#endif
            }
        }

        private void CheckSpace(int sectorCount)
        {
            lock (this.ThisLock)
            {
                if (!_spaceSectors_Initialized)
                {
                    _bitmapManager.SetLength(this.Size / CacheManager.SectorSize);

                    foreach (var clusterInfo in _settings.ClusterIndex.Values)
                    {
                        foreach (var sector in clusterInfo.Indexes)
                        {
                            _bitmapManager.Set(sector, true);
                        }
                    }

                    _spaceSectors_Initialized = true;
                }

                if (_spaceSectors.Count < sectorCount)
                {
                    for (long i = 0; i < _bitmapManager.Length; i++)
                    {
                        if (!_bitmapManager.Get(i))
                        {
                            _spaceSectors.Add(i);
                            if (_spaceSectors.Count >= sectorCount) break;
                        }
                    }
                }
            }
        }

        private void CreatingSpace(int sectorCount)
        {
            lock (this.ThisLock)
            {
                this.CheckSpace(sectorCount);
                if (sectorCount <= _spaceSectors.Count) return;

                var usingKeys = new HashSet<Key>();
                usingKeys.UnionWith(_lockedKeys.Keys);
                usingKeys.UnionWith(_settings.SeedIndex.Keys.Select(n => n.Metadata.Key));
                usingKeys.UnionWith(_settings.SeedIndex.Values.SelectMany(n => n.Keys));

                var removePairs = _settings.ClusterIndex
                    .Where(n => !usingKeys.Contains(n.Key))
                    .ToList();

                removePairs.Sort((x, y) =>
                {
                    return x.Value.UpdateTime.CompareTo(y.Value.UpdateTime);
                });

                foreach (var key in removePairs.Select(n => n.Key))
                {
                    if (sectorCount <= _spaceSectors.Count) break;

                    this.Remove(key);
                }
            }
        }

        public void Lock(Key key)
        {
            lock (this.ThisLock)
            {
                int count;

                if (_lockedKeys.TryGetValue(key, out count))
                {
                    _lockedKeys[key] = ++count;
                }
                else
                {
                    _lockedKeys[key] = 1;
                }
            }
        }

        public void Unlock(Key key)
        {
            lock (this.ThisLock)
            {
                int count;
                if (!_lockedKeys.TryGetValue(key, out count)) throw new KeyNotFoundException();

                count--;

                if (count == 0)
                {
                    _lockedKeys.Remove(key);
                }
                else
                {
                    _lockedKeys[key] = count;
                }
            }
        }

        public event Action<IEnumerable<Key>> BlockSetEvents
        {
            add
            {
                _blockSetEventQueue.Events += value;
            }
            remove
            {
                _blockSetEventQueue.Events -= value;
            }
        }

        public event Action<IEnumerable<Key>> BlockRemoveEvents
        {
            add
            {
                _blockRemoveEventQueue.Events += value;
            }
            remove
            {
                _blockRemoveEventQueue.Events -= value;
            }
        }

        public event Action<IEnumerable<string>> ShareRemoveEvents
        {
            add
            {
                _shareRemoveEventQueue.Events += value;
            }
            remove
            {
                _shareRemoveEventQueue.Events -= value;
            }
        }

        public int GetLength(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClusterIndex.ContainsKey(key))
                {
                    return _settings.ClusterIndex[key].Length;
                }

                foreach (var item in _settings.ShareIndex)
                {
                    int i = -1;

                    if (item.Value.Indexes.TryGetValue(key, out i))
                    {
                        var fileLength = new FileInfo(item.Key).Length;
                        return (int)Math.Min(fileLength - ((long)item.Value.BlockLength * i), item.Value.BlockLength);
                    }
                }

                throw new KeyNotFoundException();
            }
        }

        public bool Contains(Key key)
        {
            lock (this.ThisLock)
            {
                if (_settings.ClusterIndex.ContainsKey(key))
                {
                    return true;
                }

                _shareIndexLink_Update();

                if (_shareIndexLink.ContainsKey(key))
                {
                    return true;
                }

                return false;
            }
        }

        public bool Contains(string path)
        {
            lock (this.ThisLock)
            {
                return _settings.ShareIndex.ContainsKey(path);
            }
        }

        public IEnumerable<Key> IntersectFrom(IEnumerable<Key> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var key in collection)
                {
                    if (this.Contains(key))
                    {
                        yield return key;
                    }
                }
            }
        }

        public IEnumerable<Key> ExceptFrom(IEnumerable<Key> collection)
        {
            lock (this.ThisLock)
            {
                foreach (var key in collection)
                {
                    if (!this.Contains(key))
                    {
                        yield return key;
                    }
                }
            }
        }

        public void Remove(Key key)
        {
            lock (this.ThisLock)
            {
                ClusterInfo clusterInfo = null;

                if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                {
                    _settings.ClusterIndex.Remove(key);

                    foreach (var sector in clusterInfo.Indexes)
                    {
                        _bitmapManager.Set(sector, false);
                        if (_spaceSectors.Count < CacheManager.SpaceSectorUnit) _spaceSectors.Add(sector);
                    }

                    _blockRemoveEventQueue.Enqueue(key);
                }
            }
        }

        public void Resize(long size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

            lock (this.ThisLock)
            {
                int unit = 1024 * 1024 * 256; // 256MB
                size = CacheManager.Roundup(size, unit);

                foreach (var key in _settings.ClusterIndex.Keys.ToArray()
                    .Where(n => _settings.ClusterIndex[n].Indexes.Any(point => size < (point * CacheManager.SectorSize) + CacheManager.SectorSize))
                    .ToArray())
                {
                    this.Remove(key);
                }

                _settings.Size = CacheManager.Roundup(size, CacheManager.SectorSize);
                _fileStream.SetLength(Math.Min(_settings.Size, _fileStream.Length));

                _spaceSectors.Clear();
                _spaceSectors_Initialized = false;

                this.CheckSeeds();
            }
        }

        public void SetSeed(Seed seed, IEnumerable<Key> keys)
        {
            lock (this.ThisLock)
            {
                this.SetSeed(seed, null, keys);
            }
        }

        public void SetSeed(Seed seed, string path, IEnumerable<Key> keys)
        {
            lock (this.ThisLock)
            {
                if (_settings.SeedIndex.ContainsKey(seed)) return;

                var info = new SeedInfo();
                info.Path = path;
                info.Keys.AddRange(keys);

                _settings.SeedIndex.Add(seed, info);
            }
        }

        public void RemoveCache(Seed seed)
        {
            lock (this.ThisLock)
            {
                SeedInfo info;

                if (_settings.SeedIndex.TryGetValue(seed, out info))
                {
                    if (info.Path != null)
                    {
                        this.RemoveShare(info.Path);
                    }

                    _settings.SeedIndex.Remove(seed);
                }
            }
        }

        public void CheckInternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            // 重複するセクタを確保したブロックを検出しRemoveする。
            lock (this.ThisLock)
            {
                _bitmapManager.SetLength(this.Size / CacheManager.SectorSize);

                var keys = new List<Key>();

                foreach (var pair in _settings.ClusterIndex)
                {
                    var key = pair.Key;
                    var clusterInfo = pair.Value;

                    foreach (var sector in clusterInfo.Indexes)
                    {
                        if (!_bitmapManager.Get(sector))
                        {
                            _bitmapManager.Set(sector, true);
                        }
                        else
                        {
                            keys.Add(key);

                            break;
                        }
                    }
                }

                foreach (var key in keys)
                {
                    _settings.ClusterIndex.Remove(key);
                    _blockRemoveEventQueue.Enqueue(key);
                }

                _spaceSectors.Clear();
                _spaceSectors_Initialized = false;
            }

            // 読めないブロックを検出しRemoveする。
            {
                List<Key> list = null;

                lock (this.ThisLock)
                {
                    list = new List<Key>(_settings.ClusterIndex.Keys.Randomize());
                }

                int badBlockCount = 0;
                int checkedBlockCount = 0;
                int blockCount = list.Count;
                bool isStop;

                getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

                if (isStop) return;

                foreach (var item in list)
                {
                    checkedBlockCount++;
                    var buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = this[item];
                    }
                    catch (Exception)
                    {
                        badBlockCount++;
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    if (checkedBlockCount % 8 == 0)
                        getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

                    if (isStop) return;
                }

                getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);
            }
        }

        public void CheckExternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            // 読めないブロックを検出しRemoveする。
            {
                List<Key> list = null;

                lock (this.ThisLock)
                {
                    list = new List<Key>();

                    foreach (var item in _settings.ShareIndex.Randomize())
                    {
                        list.AddRange(item.Value.Indexes.Keys);
                    }
                }

                int badBlockCount = 0;
                int checkedBlockCount = 0;
                int blockCount = list.Count;
                bool isStop;

                getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

                if (isStop) return;

                foreach (var item in list)
                {
                    checkedBlockCount++;
                    var buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = this[item];
                    }
                    catch (Exception)
                    {
                        badBlockCount++;
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }

                    if (checkedBlockCount % 8 == 0)
                        getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);

                    if (isStop) return;
                }

                getProgressEvent.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);
            }
        }

        private void _shareIndexLink_Add(string path, ShareInfo shareInfo)
        {
            lock (this.ThisLock)
            {
                foreach (var key in shareInfo.Indexes.Keys)
                {
                    _shareIndexLink[key] = path;
                }
            }
        }

        private void _shareIndexLink_Update()
        {
            lock (this.ThisLock)
            {
                if (!_shareIndexLink_Initialized)
                {
                    _shareIndexLink.Clear();

                    foreach (var pair in _settings.ShareIndex)
                    {
                        var path = pair.Key;
                        var shareInfo = pair.Value;

                        foreach (var key in shareInfo.Indexes.Keys)
                        {
                            _shareIndexLink[key] = path;
                        }
                    }

                    _shareIndexLink_Initialized = true;
                }
            }
        }

        public KeyCollection Share(Stream stream, string path, HashAlgorithm hashAlgorithm, int blockLength)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var keys = new KeyCollection();
            var shareInfo = new ShareInfo();
            shareInfo.BlockLength = blockLength;

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(blockLength))
            {
                while (stream.Position < stream.Length)
                {
                    var length = (int)Math.Min(stream.Length - stream.Position, blockLength);
                    stream.Read(safeBuffer.Value, 0, length);

                    Key key = default(Key);

                    if (hashAlgorithm == HashAlgorithm.Sha256)
                    {
                        key = new Key(HashAlgorithm.Sha256, Sha256.ComputeHash(safeBuffer.Value, 0, length));
                    }

                    if (!shareInfo.Indexes.ContainsKey(key))
                        shareInfo.Indexes.Add(key, keys.Count);

                    keys.Add(key);
                }
            }

            lock (this.ThisLock)
            {
                if (_settings.ShareIndex.ContainsKey(path)) throw new ShareException();

                _settings.ShareIndex[path] = shareInfo;

                _shareIndexLink_Add(path, shareInfo);
            }

            foreach (var key in keys)
            {
                _blockSetEventQueue.Enqueue(key);
            }

            return keys;
        }

        public void RemoveShare(string path)
        {
            lock (this.ThisLock)
            {
                ShareInfo info;
                if (!_settings.ShareIndex.TryGetValue(path, out info)) return;

                _settings.ShareIndex.Remove(path);

                _shareIndexLink_Initialized = false;

                _shareRemoveEventQueue.Enqueue(path);
                _blockRemoveEventQueue.Enqueue(info.Indexes.Keys.ToList());
            }
        }

        public KeyCollection Encoding(Stream inStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, int blockLength, HashAlgorithm hashAlgorithm)
        {
            if (inStream == null) throw new ArgumentNullException(nameof(inStream));
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(HashAlgorithm), hashAlgorithm)) throw new ArgumentException("HashAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.Xz && cryptoAlgorithm == CryptoAlgorithm.Aes256)
            {
                byte[] aesKey = new byte[32];
                byte[] aesIv = new byte[16];
                {
                    using (MemoryStream stream = new MemoryStream(aesKey.Length + aesIv.Length))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Write(cryptoKey, 0, cryptoKey.Length);

                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Read(aesKey, 0, aesKey.Length);
                        stream.Read(aesIv, 0, aesIv.Length);
                    }
                }

                var keys = new KeyCollection();

                try
                {
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;

                        using (var outStream = new CacheManager_StreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                        using (CryptoStream cs = new CryptoStream(outStream, aes.CreateEncryptor(aesKey, aesIv), CryptoStreamMode.Write))
                        {
                            Xz.Compress(inStream, cs, _bufferManager);
                        }
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }

                    throw;
                }

                return keys;
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                var keys = new KeyCollection();

                try
                {
                    try
                    {
                        using (var outStream = new CacheManager_StreamWriter(out keys, blockLength, hashAlgorithm, this, _bufferManager))
                        using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                        {
                            int length;

                            while ((length = inStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                            {
                                outStream.Write(safeBuffer.Value, 0, length);
                            }
                        }
                    }
                    finally
                    {
                        inStream.Close();
                    }
                }
                catch (Exception)
                {
                    foreach (var key in keys)
                    {
                        this.Unlock(key);
                    }

                    throw;
                }

                return keys;
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public void Decoding(Stream outStream,
            CompressionAlgorithm compressionAlgorithm, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey, KeyCollection keys)
        {
            if (outStream == null) throw new ArgumentNullException(nameof(outStream));
            if (!Enum.IsDefined(typeof(CompressionAlgorithm), compressionAlgorithm)) throw new ArgumentException("CompressAlgorithm に存在しない列挙");
            if (!Enum.IsDefined(typeof(CryptoAlgorithm), cryptoAlgorithm)) throw new ArgumentException("CryptoAlgorithm に存在しない列挙");

            if (compressionAlgorithm == CompressionAlgorithm.Xz && cryptoAlgorithm == CryptoAlgorithm.Aes256)
            {
                byte[] aesKey = new byte[32];
                byte[] aesIv = new byte[16];
                {
                    using (MemoryStream stream = new MemoryStream(aesKey.Length + aesIv.Length))
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Write(cryptoKey, 0, cryptoKey.Length);

                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Read(aesKey, 0, aesKey.Length);
                        stream.Read(aesIv, 0, aesIv.Length);
                    }
                }

                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var inStream = new CacheManager_StreamReader(keys, this, _bufferManager))
                    using (CryptoStream cs = new CryptoStream(inStream, aes.CreateDecryptor(aesKey, aesIv), CryptoStreamMode.Read))
                    {
                        Xz.Decompress(cs, outStream, _bufferManager);
                    }
                }
            }
            else if (compressionAlgorithm == CompressionAlgorithm.None && cryptoAlgorithm == CryptoAlgorithm.None)
            {
                try
                {
                    using (var inStream = new CacheManager_StreamReader(keys, this, _bufferManager))
                    using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                    {
                        int length;

                        while ((length = inStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                        {
                            outStream.Write(safeBuffer.Value, 0, length);
                        }
                    }
                }
                finally
                {
                    outStream.Close();
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public Task<Group> ParityEncoding(KeyCollection keys, HashAlgorithm hashAlgorithm, int blockLength, CorrectionAlgorithm correctionAlgorithm, CancellationToken token)
        {
            return Task.Run(() =>
            {
                lock (_convertLock)
                {
                    if (correctionAlgorithm == CorrectionAlgorithm.None)
                    {
                        var group = new Group();
                        group.CorrectionAlgorithm = correctionAlgorithm;
                        group.InformationLength = keys.Count;
                        group.BlockLength = blockLength;
                        group.Length = keys.Sum(n => (long)this.GetLength(n));
                        group.Keys.AddRange(keys);

                        return group;
                    }
                    else if (correctionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
                    {

#if DEBUG
                        var sw = new Stopwatch();
                        sw.Start();
#endif

                        if (keys.Count > 128) throw new ArgumentOutOfRangeException(nameof(keys));

                        var buffers = new ArraySegment<byte>[keys.Count];
                        var parityBuffers = new ArraySegment<byte>[keys.Count];

                        int sumLength = 0;

                        try
                        {
                            for (int i = 0; i < buffers.Length; i++)
                            {
                                token.ThrowIfCancellationRequested();

                                var buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = this[keys[i]];
                                    int bufferLength = buffer.Count;

                                    sumLength += bufferLength;

                                    if (bufferLength > blockLength)
                                    {
                                        throw new ArgumentOutOfRangeException(nameof(blockLength));
                                    }
                                    else if (bufferLength < blockLength)
                                    {
                                        var tbuffer = new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength);
                                        Unsafe.Copy(buffer.Array, buffer.Offset, tbuffer.Array, tbuffer.Offset, buffer.Count);
                                        Unsafe.Zero(tbuffer.Array, tbuffer.Offset + buffer.Count, tbuffer.Count - buffer.Count);
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                        buffer = tbuffer;
                                    }

                                    buffers[i] = buffer;
                                }
                                catch (Exception)
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }

                                    throw;
                                }
                            }

                            for (int i = 0; i < parityBuffers.Length; i++)
                            {
                                parityBuffers[i] = new ArraySegment<byte>(_bufferManager.TakeBuffer(blockLength), 0, blockLength);
                            }

                            var indexes = new int[parityBuffers.Length];

                            for (int i = 0; i < parityBuffers.Length; i++)
                            {
                                indexes[i] = buffers.Length + i;
                            }

                            using (ReedSolomon8 reedSolomon = new ReedSolomon8(buffers.Length, buffers.Length + parityBuffers.Length, _threadCount, _bufferManager))
                            using (token.Register(() => reedSolomon.Cancel()))
                            {
                                reedSolomon.Encode(buffers, parityBuffers, indexes, blockLength);
                            }

                            token.ThrowIfCancellationRequested();

                            var parityKeys = new KeyCollection();

                            for (int i = 0; i < parityBuffers.Length; i++)
                            {
                                if (hashAlgorithm == HashAlgorithm.Sha256)
                                {
                                    var key = new Key(hashAlgorithm, Sha256.ComputeHash(parityBuffers[i]));

                                    lock (this.ThisLock)
                                    {
                                        this.Lock(key);
                                        this[key] = parityBuffers[i];
                                    }

                                    parityKeys.Add(key);
                                }
                                else
                                {
                                    throw new NotSupportedException();
                                }
                            }

                            var group = new Group();
                            group.CorrectionAlgorithm = correctionAlgorithm;
                            group.InformationLength = buffers.Length;
                            group.BlockLength = blockLength;
                            group.Length = sumLength;
                            group.Keys.AddRange(keys);
                            group.Keys.AddRange(parityKeys);

#if DEBUG
                            Debug.WriteLine(string.Format("CacheManager_ParityEncoding {0}", sw.Elapsed.ToString()));
#endif

                            return group;
                        }
                        finally
                        {
                            for (int i = 0; i < buffers.Length; i++)
                            {
                                if (buffers[i].Array != null)
                                {
                                    _bufferManager.ReturnBuffer(buffers[i].Array);
                                }
                            }

                            for (int i = 0; i < parityBuffers.Length; i++)
                            {
                                if (parityBuffers[i].Array != null)
                                {
                                    _bufferManager.ReturnBuffer(parityBuffers[i].Array);
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
            }, token);
        }

        public Task<KeyCollection> ParityDecoding(Group group, CancellationToken token)
        {
            return Task.Run(() =>
            {
                lock (_convertLock)
                {
                    if (group.BlockLength > 1024 * 1024 * 4) throw new ArgumentOutOfRangeException();

                    if (group.CorrectionAlgorithm == CorrectionAlgorithm.None)
                    {
                        return new KeyCollection(group.Keys);
                    }
                    else if (group.CorrectionAlgorithm == CorrectionAlgorithm.ReedSolomon8)
                    {
                        var buffers = new ArraySegment<byte>[group.InformationLength];

                        try
                        {
                            var indexes = new int[group.InformationLength];

                            int count = 0;

                            for (int i = 0; i < group.Keys.Count && count < group.InformationLength; i++)
                            {
                                token.ThrowIfCancellationRequested();

                                if (!this.Contains(group.Keys[i])) continue;

                                var buffer = new ArraySegment<byte>();

                                try
                                {
                                    buffer = this[group.Keys[i]];
                                    int bufferLength = buffer.Count;

                                    if (bufferLength > group.BlockLength)
                                    {
                                        throw new ArgumentOutOfRangeException(nameof(group), "BlockLength");
                                    }
                                    else if (bufferLength < group.BlockLength)
                                    {
                                        var tbuffer = new ArraySegment<byte>(_bufferManager.TakeBuffer(group.BlockLength), 0, group.BlockLength);
                                        Unsafe.Copy(buffer.Array, buffer.Offset, tbuffer.Array, tbuffer.Offset, buffer.Count);
                                        Unsafe.Zero(tbuffer.Array, tbuffer.Offset + buffer.Count, tbuffer.Count - buffer.Count);
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                        buffer = tbuffer;
                                    }

                                    indexes[count] = i;
                                    buffers[count] = buffer;

                                    count++;
                                }
                                catch (BlockNotFoundException)
                                {

                                }
                                catch (Exception)
                                {
                                    if (buffer.Array != null)
                                    {
                                        _bufferManager.ReturnBuffer(buffer.Array);
                                    }

                                    throw;
                                }
                            }

                            if (count < group.InformationLength) throw new BlockNotFoundException();

                            using (ReedSolomon8 reedSolomon = new ReedSolomon8(group.InformationLength, group.Keys.Count, _threadCount, _bufferManager))
                            using (token.Register(() => reedSolomon.Cancel()))
                            {
                                reedSolomon.Decode(buffers, indexes, group.BlockLength);
                            }

                            token.ThrowIfCancellationRequested();

                            long length = group.Length;

                            for (int i = 0; i < group.InformationLength; length -= group.BlockLength, i++)
                            {
                                this[group.Keys[i]] = new ArraySegment<byte>(buffers[i].Array, buffers[i].Offset, (int)Math.Min(buffers[i].Count, length));
                            }
                        }
                        finally
                        {
                            for (int i = 0; i < buffers.Length; i++)
                            {
                                if (buffers[i].Array != null)
                                {
                                    _bufferManager.ReturnBuffer(buffers[i].Array);
                                }
                            }
                        }

                        var keys = new KeyCollection();

                        for (int i = 0; i < group.InformationLength; i++)
                        {
                            keys.Add(group.Keys[i]);
                        }

                        return new KeyCollection(keys);
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
            }, token);
        }

        private byte[] _sectorBuffer = new byte[CacheManager.SectorSize];

        public ArraySegment<byte> this[Key key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    // Cache
                    {
                        ClusterInfo clusterInfo = null;

                        if (_settings.ClusterIndex.TryGetValue(key, out clusterInfo))
                        {
                            clusterInfo.UpdateTime = DateTime.UtcNow;

                            byte[] buffer = _bufferManager.TakeBuffer(clusterInfo.Length);

                            try
                            {
                                for (int i = 0, remain = clusterInfo.Length; i < clusterInfo.Indexes.Length; i++, remain -= CacheManager.SectorSize)
                                {
                                    try
                                    {
                                        long posision = clusterInfo.Indexes[i] * CacheManager.SectorSize;

                                        if (posision > _fileStream.Length)
                                        {
                                            this.Remove(key);

                                            throw new BlockNotFoundException();
                                        }

                                        if (_fileStream.Position != posision)
                                        {
                                            _fileStream.Seek(posision, SeekOrigin.Begin);
                                        }

                                        int length = Math.Min(remain, CacheManager.SectorSize);

                                        {
                                            _fileStream.Read(_sectorBuffer, 0, _sectorBuffer.Length);

                                            Unsafe.Copy(_sectorBuffer, 0, buffer, CacheManager.SectorSize * i, length);
                                        }
                                    }
                                    catch (ArgumentOutOfRangeException)
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }
                                    catch (IOException)
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }
                                }

                                if (key.HashAlgorithm == HashAlgorithm.Sha256)
                                {
                                    if (!Unsafe.Equals(Sha256.ComputeHash(buffer, 0, clusterInfo.Length), key.Hash))
                                    {
                                        this.Remove(key);

                                        throw new BlockNotFoundException();
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                return new ArraySegment<byte>(buffer, 0, clusterInfo.Length);
                            }
                            catch (Exception)
                            {
                                _bufferManager.ReturnBuffer(buffer);

                                throw;
                            }
                        }
                    }

                    // Share
                    {
                        string path = null;

                        _shareIndexLink_Update();

                        if (_shareIndexLink.TryGetValue(key, out path))
                        {
                            var shareInfo = _settings.ShareIndex[path];

                            byte[] buffer = _bufferManager.TakeBuffer(shareInfo.BlockLength);

                            try
                            {
                                int length;

                                try
                                {
                                    using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                                    {
                                        stream.Seek((long)shareInfo.Indexes[key] * shareInfo.BlockLength, SeekOrigin.Begin);

                                        length = (int)Math.Min(stream.Length - stream.Position, shareInfo.BlockLength);
                                        stream.Read(buffer, 0, length);
                                    }
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    throw new BlockNotFoundException();
                                }
                                catch (IOException)
                                {
                                    throw new BlockNotFoundException();
                                }

                                if (key.HashAlgorithm == HashAlgorithm.Sha256)
                                {
                                    if (!Unsafe.Equals(Sha256.ComputeHash(buffer, 0, length), key.Hash))
                                    {
                                        this.RemoveShare(path);

                                        throw new BlockNotFoundException();
                                    }
                                }
                                else
                                {
                                    throw new FormatException();
                                }

                                return new ArraySegment<byte>(buffer, 0, length);
                            }
                            catch (Exception)
                            {
                                _bufferManager.ReturnBuffer(buffer);

                                throw;
                            }
                        }
                    }

                    throw new BlockNotFoundException();
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value.Count > 1024 * 1024 * 32) throw new BadBlockException();

                    if (key.HashAlgorithm == HashAlgorithm.Sha256)
                    {
                        if (!Unsafe.Equals(Sha256.ComputeHash(value), key.Hash)) throw new BadBlockException();
                    }
                    else
                    {
                        throw new FormatException();
                    }

                    if (this.Contains(key)) return;

                    List<long> sectorList = null;

                    try
                    {
                        int count = (value.Count + (CacheManager.SectorSize - 1)) / CacheManager.SectorSize;

                        sectorList = new List<long>(count);

                        if (_spaceSectors.Count < count)
                        {
                            this.CreatingSpace(CacheManager.SpaceSectorUnit);
                        }

                        if (_spaceSectors.Count < count) throw new SpaceNotFoundException();

                        sectorList.AddRange(_spaceSectors.Take(count));

                        foreach (var sector in sectorList)
                        {
                            _bitmapManager.Set(sector, true);
                            _spaceSectors.Remove(sector);
                        }

                        for (int i = 0, remain = value.Count; i < sectorList.Count && 0 < remain; i++, remain -= CacheManager.SectorSize)
                        {
                            long posision = sectorList[i] * CacheManager.SectorSize;

                            if ((_fileStream.Length < posision + CacheManager.SectorSize))
                            {
                                int unit = 1024 * 1024 * 256; // 256MB
                                long size = CacheManager.Roundup((posision + CacheManager.SectorSize), unit);

                                _fileStream.SetLength(Math.Min(size, this.Size));
                            }

                            if (_fileStream.Position != posision)
                            {
                                _fileStream.Seek(posision, SeekOrigin.Begin);
                            }

                            int length = Math.Min(remain, CacheManager.SectorSize);

                            {
                                Unsafe.Copy(value.Array, value.Offset + (CacheManager.SectorSize * i), _sectorBuffer, 0, length);
                                Unsafe.Zero(_sectorBuffer, length, _sectorBuffer.Length - length);

                                _fileStream.Write(_sectorBuffer, 0, _sectorBuffer.Length);
                            }
                        }

                        _fileStream.Flush();
                    }
                    catch (SpaceNotFoundException e)
                    {
                        Log.Error(e);

                        throw e;
                    }
                    catch (IOException e)
                    {
                        Log.Error(e);

                        throw e;
                    }

                    var clusterInfo = new ClusterInfo();
                    clusterInfo.Indexes = sectorList.ToArray();
                    clusterInfo.Length = value.Count;
                    clusterInfo.UpdateTime = DateTime.UtcNow;
                    _settings.ClusterIndex[key] = clusterInfo;

                    _blockSetEventQueue.Enqueue(key);
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                _shareIndexLink_Initialized = false;

                _watchTimer.Change(new TimeSpan(0, 0, 0), new TimeSpan(0, 5, 0));
                _checkTimer.Change(new TimeSpan(0, 0, 0), new TimeSpan(0, 30, 0));
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        public Key[] ToArray()
        {
            lock (this.ThisLock)
            {
                int count = 0;

                {
                    count += _settings.ClusterIndex.Keys.Count;

                    foreach (var shareInfo in _settings.ShareIndex.Values)
                    {
                        count += shareInfo.Indexes.Keys.Count;
                    }
                }

                var list = new List<Key>(count);

                {
                    list.AddRange(_settings.ClusterIndex.Keys);

                    foreach (var shareInfo in _settings.ShareIndex.Values)
                    {
                        list.AddRange(shareInfo.Indexes.Keys);
                    }
                }

                return list.ToArray();
            }
        }

        #region IEnumerable<Key>

        public IEnumerator<Key> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                foreach (var key in _settings.ClusterIndex.Keys)
                {
                    yield return key;
                }

                foreach (var shareInfo in _settings.ShareIndex.Values)
                {
                    foreach (var key in shareInfo.Indexes.Keys)
                    {
                        yield return key;
                    }
                }
            }
        }

        #endregion

        #region IEnumerable

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<long>() { Name = "Size", Value = (long)1024 * 1024 * 1024 * 256 },
                    new Library.Configuration.SettingContent<LockedHashDictionary<Key, ClusterInfo>>() { Name = "ClusterIndex", Value = new LockedHashDictionary<Key, ClusterInfo>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<string, ShareInfo>>() { Name = "ShareIndex", Value = new LockedHashDictionary<string, ShareInfo>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<Seed, SeedInfo>>() { Name = "SeedIndex", Value = new LockedHashDictionary<Seed, SeedInfo>() },
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

            public long Size
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (long)this["Size"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["Size"] = value;
                    }
                }
            }

            public LockedHashDictionary<Key, ClusterInfo> ClusterIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Key, ClusterInfo>)this["ClusterIndex"];
                    }
                }
            }

            public LockedHashDictionary<string, ShareInfo> ShareIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<string, ShareInfo>)this["ShareIndex"];
                    }
                }
            }

            public LockedHashDictionary<Seed, SeedInfo> SeedIndex
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Seed, SeedInfo>)this["SeedIndex"];
                    }
                }
            }
        }

        [DataContract(Name = "ClusterInfo")]
        private class ClusterInfo
        {
            private long[] _indexes;
            private int _length;
            private DateTime _updateTime;

            [DataMember(Name = "Indexes")]
            public long[] Indexes
            {
                get
                {
                    return _indexes;
                }
                set
                {
                    _indexes = value;
                }
            }

            [DataMember(Name = "Length")]
            public int Length
            {
                get
                {
                    return _length;
                }
                set
                {
                    _length = value;
                }
            }

            [DataMember(Name = "UpdateTime")]
            public DateTime UpdateTime
            {
                get
                {
                    return _updateTime;
                }
                set
                {
                    var utc = value.ToUniversalTime();
                    _updateTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        [DataContract(Name = "ShareInfo")]
        private class ShareInfo
        {
            private Dictionary<Key, int> _indexes;
            private int _blockLength;

            [DataMember(Name = "Indexes")]
            public Dictionary<Key, int> Indexes
            {
                get
                {
                    if (_indexes == null)
                        _indexes = new Dictionary<Key, int>();

                    return _indexes;
                }
            }

            [DataMember(Name = "BlockLength")]
            public int BlockLength
            {
                get
                {
                    return _blockLength;
                }
                set
                {
                    _blockLength = value;
                }
            }
        }

        [DataContract(Name = "SeedInfo")]
        private class SeedInfo
        {
            private string _path;
            private KeyCollection _keys;

            [DataMember(Name = "Path")]
            public string Path
            {
                get
                {
                    return _path;
                }
                set
                {
                    _path = value;
                }
            }

            [DataMember(Name = "Keys")]
            public KeyCollection Keys
            {
                get
                {
                    if (_keys == null)
                        _keys = new KeyCollection();

                    return _keys;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_fileStream != null)
                {
                    try
                    {
                        _fileStream.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _fileStream = null;
                }

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

    [Serializable]
    class CacheManagerException : ManagerException
    {
        public CacheManagerException() : base() { }
        public CacheManagerException(string message) : base(message) { }
        public CacheManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class SpaceNotFoundException : CacheManagerException
    {
        public SpaceNotFoundException() : base() { }
        public SpaceNotFoundException(string message) : base(message) { }
        public SpaceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BlockNotFoundException : CacheManagerException
    {
        public BlockNotFoundException() : base() { }
        public BlockNotFoundException(string message) : base(message) { }
        public BlockNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class StopException : CacheManagerException
    {
        public StopException() : base() { }
        public StopException(string message) : base(message) { }
        public StopException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BadBlockException : CacheManagerException
    {
        public BadBlockException() : base() { }
        public BadBlockException(string message) : base(message) { }
        public BadBlockException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class ShareException : CacheManagerException
    {
        public ShareException() : base() { }
        public ShareException(string message) : base(message) { }
        public ShareException(string message, Exception innerException) : base(message, innerException) { }
    }
}
