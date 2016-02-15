using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class DownloadManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        public VolatileHashDictionary<BroadcastMetadata, BroadcastMessage> _cache_Metadata_BroadcastMessage_Pairs;
        public VolatileHashDictionary<string, Dictionary<UnicastMetadata, UnicastMessage>> _cache_Metadata_UnicastMessage_Pairs_Dictionary;
        public VolatileHashDictionary<Tag, Dictionary<MulticastMetadata, MulticastMessage>> _cache_Metadata_MulticastMessage_Pairs_Dictionary;

        private WatchTimer _watchTimer;

        private Settings _settings;

        private ManagerState _state = ManagerState.Stop;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha256;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;

            _cache_Metadata_BroadcastMessage_Pairs = new VolatileHashDictionary<BroadcastMetadata, BroadcastMessage>(new TimeSpan(0, 30, 0));
            _cache_Metadata_UnicastMessage_Pairs_Dictionary = new VolatileHashDictionary<string, Dictionary<UnicastMetadata, UnicastMessage>>(new TimeSpan(0, 30, 0));
            _cache_Metadata_MulticastMessage_Pairs_Dictionary = new VolatileHashDictionary<Tag, Dictionary<MulticastMetadata, MulticastMessage>>(new TimeSpan(0, 30, 0));

            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 0, 30));

            _settings = new Settings(this.ThisLock);

            _connectionsManager.GetLockSignaturesEvent = (object sender) =>
            {
                var signatures = new HashSet<string>();
                signatures.UnionWith(_settings.TrustSignatures);
                signatures.UnionWith(_cache_Metadata_BroadcastMessage_Pairs.Keys.Select(n => n.Certificate.ToString()));
                signatures.UnionWith(_cache_Metadata_UnicastMessage_Pairs_Dictionary.Keys);

                return signatures;
            };

            _connectionsManager.GetLockTagsEvent = (object sender) =>
            {
                var tags = new HashSet<Tag>();
                tags.UnionWith(_cache_Metadata_MulticastMessage_Pairs_Dictionary.Keys);

                return tags;
            };
        }

        private void WatchTimer()
        {
            lock (this.ThisLock)
            {
                _cache_Metadata_BroadcastMessage_Pairs.TrimExcess();
                _cache_Metadata_UnicastMessage_Pairs_Dictionary.TrimExcess();
                _cache_Metadata_MulticastMessage_Pairs_Dictionary.TrimExcess();
            }
        }

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.TrustSignatures.ToArray();
                }
            }
        }

        public void SetTrustSignatures(IEnumerable<string> signatures)
        {
            lock (this.ThisLock)
            {
                lock (_settings.TrustSignatures.ThisLock)
                {
                    _settings.TrustSignatures.Clear();
                    _settings.TrustSignatures.UnionWith(new SignatureCollection(signatures));
                }
            }
        }

        public BroadcastMessage GetBroadcastMessage(string signature)
        {
            lock (this.ThisLock)
            {
                BroadcastMessage broadcastMessage = null;

                {
                    var metadata = _connectionsManager.GetBroadcastMetadata(signature);
                    if (metadata == null) goto End;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    if (!_cache_Metadata_BroadcastMessage_Pairs.TryGetValue(metadata, out broadcastMessage))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var message = ContentConverter.FromBroadcastMessageBlock(buffer);

                            if (metadata.CreationTime != message.CreationTime
                                || metadata.Certificate.ToString() != message.Certificate.ToString()) goto End;

                            _cache_Metadata_BroadcastMessage_Pairs[metadata] = message;

                            broadcastMessage = message;
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

                End: ;
                }

                return broadcastMessage;
            }
        }

        public IEnumerable<UnicastMessage> GetUnicastMessages(string signature, ExchangePrivateKey exchangePrivateKey)
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (exchangePrivateKey == null) throw new ArgumentNullException(nameof(exchangePrivateKey));

            lock (this.ThisLock)
            {
                Dictionary<UnicastMetadata, UnicastMessage> dic;

                if (!_cache_Metadata_UnicastMessage_Pairs_Dictionary.TryGetValue(signature, out dic))
                {
                    dic = new Dictionary<UnicastMetadata, UnicastMessage>();
                    _cache_Metadata_UnicastMessage_Pairs_Dictionary[signature] = dic;
                }

                var metadatas = new HashSet<UnicastMetadata>(_connectionsManager.GetUnicastMetadatas(signature));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var unicastMessages = new List<UnicastMessage>();

                foreach (var metadata in metadatas)
                {
                    if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString())) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    UnicastMessage unicastMessage;

                    if (!dic.TryGetValue(metadata, out unicastMessage))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var message = ContentConverter.FromUnicastMessageBlock(buffer, exchangePrivateKey);

                            if (metadata.Signature != message.Signature
                                || metadata.CreationTime != message.CreationTime
                                || metadata.Certificate.ToString() != message.Certificate.ToString()) continue;

                            dic[metadata] = message;

                            unicastMessage = message;
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

                    if (unicastMessage != null) unicastMessages.Add(unicastMessage);
                }

                return unicastMessages;
            }
        }

        public IEnumerable<MulticastMessage> GetMulticastMessages(Tag tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            lock (this.ThisLock)
            {
                Dictionary<MulticastMetadata, MulticastMessage> dic;

                if (!_cache_Metadata_MulticastMessage_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<MulticastMetadata, MulticastMessage>();
                    _cache_Metadata_MulticastMessage_Pairs_Dictionary[tag] = dic;
                }

                var metadatas = new HashSet<MulticastMetadata>(_connectionsManager.GetMulticastMetadatas(tag));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var multicastMessages = new List<MulticastMessage>();

                foreach (var metadata in metadatas)
                {
                    if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    MulticastMessage multicastMessage;

                    if (!dic.TryGetValue(metadata, out multicastMessage))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var message = ContentConverter.FromMulticastMessageBlock(buffer);

                            if (metadata.Tag != message.Tag
                                || metadata.CreationTime != message.CreationTime
                                || metadata.Certificate.ToString() != message.Certificate.ToString()) continue;

                            dic[metadata] = message;

                            multicastMessage = message;
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

                    if (multicastMessage != null) multicastMessages.Add(multicastMessage);
                }

                return multicastMessages;
            }
        }

        public override ManagerState State
        {
            get
            {
                lock (this.ThisLock)
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
                lock (this.ThisLock)
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
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);
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

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() { 
                    new Library.Configuration.SettingContent<LockedHashSet<string>>() { Name = "TrustSignatures", Value = new LockedHashSet<string>() },
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

            public LockedHashSet<string> TrustSignatures
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<string>)this["TrustSignatures"];
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
    class DownloadManagerException : ManagerException
    {
        public DownloadManagerException() : base() { }
        public DownloadManagerException(string message) : base(message) { }
        public DownloadManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class DecodeException : DownloadManagerException
    {
        public DecodeException() : base() { }
        public DecodeException(string message) : base(message) { }
        public DecodeException(string message, Exception innerException) : base(message, innerException) { }
    }
}
