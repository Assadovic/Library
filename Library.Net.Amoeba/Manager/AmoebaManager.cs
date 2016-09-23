using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Library.Security;

namespace Library.Net.Amoeba
{
    public delegate bool CheckUriEventHandler(object sender, string uri);

    // 色々力技が必要になり個々のクラスが見苦しので、このクラスで覆う

    public sealed class AmoebaManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _bitmapPath;
        private string _blocksPath;
        private BufferManager _bufferManager;

        private ClientManager _clientManager;
        private ServerManager _serverManager;
        private BitmapManager _bitmapManager;
        private CacheManager _cacheManager;
        private ConnectionsManager _connectionsManager;
        private DownloadManager _downloadManager;
        private UploadManager _uploadManager;
        private BackgroundDownloadManager _backgroundDownloadManager;
        private BackgroundUploadManager _backgroundUploadManager;

        private volatile ManagerState _state = ManagerState.Stop;
        private volatile ManagerState _encodeState = ManagerState.Stop;
        private volatile ManagerState _decodeState = ManagerState.Stop;

        private CreateCapEventHandler _createCapEvent;
        private AcceptCapEventHandler _acceptCapEvent;
        private CheckUriEventHandler _checkUriEvent;

        private bool _isLoaded = false;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public AmoebaManager(string bitmapPath, string blocksPath, BufferManager bufferManager)
        {
            _bitmapPath = bitmapPath;
            _blocksPath = blocksPath;
            _bufferManager = bufferManager;

            _clientManager = new ClientManager(_bufferManager);
            _serverManager = new ServerManager(_bufferManager);
            _bitmapManager = new BitmapManager(_bitmapPath, _bufferManager);
            _cacheManager = new CacheManager(_blocksPath, _bitmapManager, _bufferManager);
            _connectionsManager = new ConnectionsManager(_clientManager, _serverManager, _cacheManager, _bufferManager);
            _downloadManager = new DownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _uploadManager = new UploadManager(_connectionsManager, _cacheManager, _bufferManager);
            _backgroundDownloadManager = new BackgroundDownloadManager(_connectionsManager, _cacheManager, _bufferManager);
            _backgroundUploadManager = new BackgroundUploadManager(_connectionsManager, _cacheManager, _bufferManager);

            _clientManager.CreateCapEvent = (object sender, string uri) =>
            {
                return _createCapEvent?.Invoke(this, uri);
            };

            _serverManager.AcceptCapEvent = (object sender, out string uri) =>
            {
                uri = null;
                return _acceptCapEvent?.Invoke(this, out uri);
            };

            _clientManager.CheckUriEvent = (object sender, string uri) =>
            {
                return _checkUriEvent?.Invoke(this, uri) ?? true;
            };

            _serverManager.CheckUriEvent = (object sender, string uri) =>
            {
                return _checkUriEvent?.Invoke(this, uri) ?? true;
            };
        }

        private void Check()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new AmoebaManagerException("AmoebaManager is not loaded.");
        }

        public CreateCapEventHandler CreateCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _createCapEvent = value;
                }
            }
        }

        public AcceptCapEventHandler AcceptCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _acceptCapEvent = value;
                }
            }
        }

        public CheckUriEventHandler CheckUriEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _checkUriEvent = value;
                }
            }
        }

        public Information Information
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    var contexts = new List<InformationContext>();
                    contexts.AddRange(_serverManager.Information);
                    contexts.AddRange(_cacheManager.Information);
                    contexts.AddRange(_connectionsManager.Information);
                    contexts.AddRange(_uploadManager.Information);
                    contexts.AddRange(_downloadManager.Information);

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ConnectionInformation
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionInformation;
                }
            }
        }

        public IEnumerable<Information> ShareInformation
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _cacheManager.ShareInformation;
                }
            }
        }

        public IEnumerable<Information> DownloadingInformation
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _downloadManager.DownloadingInformation;
                }
            }
        }

        public IEnumerable<Information> UploadingInformation
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _uploadManager.UploadingInformation;
                }
            }
        }

        public Node BaseNode
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.BaseNode;
                }
            }
        }

        public IEnumerable<Node> OtherNodes
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.OtherNodes;
                }
            }
        }

        public int ConnectionCountLimit
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.ConnectionCountLimit;
                }
            }
            set
            {
                this.Check();

                lock (this.ThisLock)
                {
                    _connectionsManager.ConnectionCountLimit = value;
                }
            }
        }

        public int BandwidthLimit
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.BandwidthLimit;
                }
            }
            set
            {
                this.Check();

                lock (this.ThisLock)
                {
                    _connectionsManager.BandwidthLimit = value;
                }
            }
        }

        public long ReceivedByteCount
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.ReceivedByteCount;
                }
            }
        }

        public long SentByteCount
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _connectionsManager.SentByteCount;
                }
            }
        }

        public ConnectionFilterCollection Filters
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _clientManager.Filters;
                }
            }
        }

        public UriCollection ListenUris
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _serverManager.ListenUris;
                }
            }
        }

        public IEnumerable<Seed> CacheSeeds
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _cacheManager.CacheSeeds;
                }
            }
        }

        public long Size
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _cacheManager.Size;
                }
            }
        }

        public SeedCollection DownloadedSeeds
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _downloadManager.DownloadedSeeds;
                }
            }
        }

        public string DownloadDirectory
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _downloadManager.BaseDirectory;
                }
            }
            set
            {
                this.Check();

                lock (this.ThisLock)
                {
                    _downloadManager.BaseDirectory = value;
                }
            }
        }

        public SeedCollection UploadedSeeds
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _uploadManager.UploadedSeeds;
                }
            }
        }

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                this.Check();

                lock (this.ThisLock)
                {
                    return _backgroundDownloadManager.TrustSignatures;
                }
            }
        }

        public void SetBaseNode(Node baseNode)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _connectionsManager.SetBaseNode(baseNode);
            }
        }

        public void SetOtherNodes(IEnumerable<Node> nodes)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _connectionsManager.SetOtherNodes(nodes);
            }
        }

        public void Resize(long size)
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.EncodeState == ManagerState.Start)
                {
                    _uploadManager.EncodeStop();
                }

                if (this.DecodeState == ManagerState.Start)
                {
                    _downloadManager.DecodeStop();
                }

                if (this.State == ManagerState.Start)
                {
                    _backgroundUploadManager.Stop();
                    _backgroundDownloadManager.Stop();
                    _uploadManager.Stop();
                    _downloadManager.Stop();
                }

                _cacheManager.Resize(size);

                if (this.State == ManagerState.Start)
                {
                    _downloadManager.Start();
                    _uploadManager.Start();
                    _backgroundDownloadManager.Start();
                    _backgroundUploadManager.Start();
                }

                if (this.DecodeState == ManagerState.Start)
                {
                    _downloadManager.DecodeStart();
                }

                if (this.EncodeState == ManagerState.Start)
                {
                    _uploadManager.EncodeStart();
                }
            }
        }

        public void SetTrustSignatures(IEnumerable<string> signatures)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundDownloadManager.SetTrustSignatures(signatures);
            }
        }

        public void CheckInternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            this.Check();

            _cacheManager.CheckInternalBlocks((object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop) =>
            {
                isStop = false;
                getProgressEvent?.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);
            });
        }

        public void CheckExternalBlocks(CheckBlocksProgressEventHandler getProgressEvent)
        {
            this.Check();

            _cacheManager.CheckExternalBlocks((object sender, int badBlockCount, int checkedBlockCount, int blockCount, out bool isStop) =>
            {
                isStop = false;
                getProgressEvent?.Invoke(this, badBlockCount, checkedBlockCount, blockCount, out isStop);
            });
        }

        public void Download(Seed seed, int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _downloadManager.Download(seed, priority);
            }
        }

        public void Download(Seed seed, string path, int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _downloadManager.Download(seed, path, priority);
            }
        }

        public void Upload(string filePath,
            string name,
            IEnumerable<string> keywords,
            DigitalSignature digitalSignature,
            int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _uploadManager.Upload(filePath,
                    name,
                    keywords,
                    digitalSignature,
                    priority);
            }
        }

        public void Share(string filePath,
            string name,
            IEnumerable<string> keywords,
            DigitalSignature digitalSignature,
            int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _uploadManager.Share(filePath,
                    name,
                    keywords,
                    digitalSignature,
                    priority);
            }
        }

        public void RemoveDownload(int id)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _downloadManager.Remove(id);
            }
        }

        public void RemoveUpload(int id)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _uploadManager.Remove(id);
            }
        }

        public void RemoveShare(string path)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _cacheManager.RemoveShare(path);
            }
        }

        public void RemoveCache(Seed seed)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _cacheManager.RemoveCache(seed);
            }
        }

        public void ResetDownload(int id)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _downloadManager.Reset(id);
            }
        }

        public void ResetUpload(int id)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _uploadManager.Reset(id);
            }
        }

        public void SetDownloadPriority(int id, int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _downloadManager.SetPriority(id, priority);
            }
        }

        public void SetUploadPriority(int id, int priority)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _uploadManager.SetPriority(id, priority);
            }
        }

        public Link GetLink(string signature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetLink(signature);
            }
        }

        public Profile GetProfile(string signature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetProfile(signature);
            }
        }

        public Store GetStore(string signature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetStore(signature);
            }
        }

        public IEnumerable<Information> GetUnicastMessages(string signature, ExchangePrivateKey exchangePrivateKey)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetUnicastMessages(signature, exchangePrivateKey);
            }
        }

        public IEnumerable<Information> GetMulticastMessages(Tag tag, int limit)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetMulticastMessages(tag, limit);
            }
        }

        public IEnumerable<Information> GetMulticastWebsites(Tag tag, int limit)
        {
            this.Check();

            lock (this.ThisLock)
            {
                return _backgroundDownloadManager.GetMulticastWebsites(tag, limit);
            }
        }

        public void Upload(Link link, DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Upload(link, digitalSignature);
            }
        }

        public void Upload(Profile profile, DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Upload(profile, digitalSignature);
            }
        }

        public void Upload(Store store, DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.Upload(store, digitalSignature);
            }
        }

        public void UnicastUpload(string signature,
            Message message,

            ExchangePublicKey exchangePublicKey,
            DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.UnicastUpload(signature, message, exchangePublicKey, digitalSignature);
            }
        }

        public void MulticastUpload(Tag tag,
            Message message,

            int miningLimit,
            TimeSpan miningTime,
            DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.MulticastUpload(tag, message, miningLimit, miningTime, digitalSignature);
            }
        }

        public void MulticastUpload(Tag tag,
            Website website,

            DigitalSignature digitalSignature)
        {
            this.Check();

            lock (this.ThisLock)
            {
                _backgroundUploadManager.MulticastUpload(tag, website, digitalSignature);
            }
        }

        public override ManagerState State
        {
            get
            {
                this.Check();

                return _state;
            }
        }

        public ManagerState EncodeState
        {
            get
            {
                this.Check();

                return _encodeState;
            }
        }

        public ManagerState DecodeState
        {
            get
            {
                this.Check();

                return _decodeState;
            }
        }

        public override void Start()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Start) return;
                _state = ManagerState.Start;

                _connectionsManager.Start();
                _downloadManager.Start();
                _uploadManager.Start();
                _backgroundDownloadManager.Start();
                _backgroundUploadManager.Start();
            }
        }

        public override void Stop()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.State == ManagerState.Stop) return;
                _state = ManagerState.Stop;

                _backgroundUploadManager.Stop();
                _backgroundDownloadManager.Stop();
                _uploadManager.Stop();
                _downloadManager.Stop();
                _connectionsManager.Stop();
            }
        }

        public void EncodeStart()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.EncodeState == ManagerState.Start) return;
                _encodeState = ManagerState.Start;

                _uploadManager.EncodeStart();
            }
        }

        public void EncodeStop()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.EncodeState == ManagerState.Stop) return;
                _encodeState = ManagerState.Stop;

                _uploadManager.EncodeStop();
            }
        }

        public void DecodeStart()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Start) return;
                _decodeState = ManagerState.Start;

                _downloadManager.DecodeStart();
            }
        }

        public void DecodeStop()
        {
            this.Check();

            lock (this.ThisLock)
            {
                if (this.DecodeState == ManagerState.Stop) return;
                _decodeState = ManagerState.Stop;

                _downloadManager.DecodeStop();
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_isLoaded) throw new AmoebaManagerException("AmoebaManager was already loaded.");
                _isLoaded = true;

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                _clientManager.Load(Path.Combine(directoryPath, "ClientManager"));
                _serverManager.Load(Path.Combine(directoryPath, "ServerManager"));
                _bitmapManager.Load(Path.Combine(directoryPath, "BitmapManager"));
                _cacheManager.Load(Path.Combine(directoryPath, "CacheManager"));
                _connectionsManager.Load(Path.Combine(directoryPath, "ConnectionsManager"));

                var tasks = new List<Task>();

                tasks.Add(Task.Run(() => _downloadManager.Load(Path.Combine(directoryPath, "DownloadManager"))));
                tasks.Add(Task.Run(() => _uploadManager.Load(Path.Combine(directoryPath, "UploadManager"))));
                tasks.Add(Task.Run(() => _backgroundDownloadManager.Load(Path.Combine(directoryPath, "BackgroundDownloadManager"))));
                tasks.Add(Task.Run(() => _backgroundUploadManager.Load(Path.Combine(directoryPath, "BackgroundUploadManager"))));

                Task.WaitAll(tasks.ToArray());

                stopwatch.Stop();
                Debug.WriteLine("Settings Load {0} {1}", Path.GetFileName(directoryPath), stopwatch.ElapsedMilliseconds);
            }
        }

        public void Save(string directoryPath)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_isLoaded) throw new AmoebaManagerException("AmoebaManager is not loaded.");

            lock (this.ThisLock)
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var tasks = new List<Task>();

                tasks.Add(Task.Run(() => _backgroundUploadManager.Save(Path.Combine(directoryPath, "BackgroundUploadManager"))));
                tasks.Add(Task.Run(() => _backgroundDownloadManager.Save(Path.Combine(directoryPath, "BackgroundDownloadManager"))));
                tasks.Add(Task.Run(() => _uploadManager.Save(Path.Combine(directoryPath, "UploadManager"))));
                tasks.Add(Task.Run(() => _downloadManager.Save(Path.Combine(directoryPath, "DownloadManager"))));

                Task.WaitAll(tasks.ToArray());

                _connectionsManager.Save(Path.Combine(directoryPath, "ConnectionsManager"));
                _cacheManager.Save(Path.Combine(directoryPath, "CacheManager"));
                _bitmapManager.Save(Path.Combine(directoryPath, "BitmapManager"));
                _serverManager.Save(Path.Combine(directoryPath, "ServerManager"));
                _clientManager.Save(Path.Combine(directoryPath, "ClientManager"));

                stopwatch.Stop();
                Debug.WriteLine("Settings Save {0} {1}", Path.GetFileName(directoryPath), stopwatch.ElapsedMilliseconds);
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _backgroundUploadManager.Dispose();
                _backgroundDownloadManager.Dispose();
                _uploadManager.Dispose();
                _downloadManager.Dispose();
                _connectionsManager.Dispose();
                _cacheManager.Dispose();
                _bitmapManager.Dispose();
                _serverManager.Dispose();
                _clientManager.Dispose();
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
    class AmoebaManagerException : StateManagerException
    {
        public AmoebaManagerException() : base() { }
        public AmoebaManagerException(string message) : base(message) { }
        public AmoebaManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
