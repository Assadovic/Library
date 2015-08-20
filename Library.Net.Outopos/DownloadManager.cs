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

        public VolatileHashDictionary<ProfileMetadata, Profile> _cache_Metadata_Profile_Pairs;
        public VolatileHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>> _cache_Metadata_SignatureMessage_Pairs_Dictionary;
        public VolatileHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>> _cache_Metadata_WikiDocument_Pairs_Dictionary;
        public VolatileHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>> _cache_Metadata_ChatMessage_Pairs_Dictionary;

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

            _cache_Metadata_Profile_Pairs = new VolatileHashDictionary<ProfileMetadata, Profile>(new TimeSpan(0, 30, 0));
            _cache_Metadata_SignatureMessage_Pairs_Dictionary = new VolatileHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>>(new TimeSpan(0, 30, 0));
            _cache_Metadata_WikiDocument_Pairs_Dictionary = new VolatileHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>>(new TimeSpan(0, 30, 0));
            _cache_Metadata_ChatMessage_Pairs_Dictionary = new VolatileHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>>(new TimeSpan(0, 30, 0));

            _watchTimer = new WatchTimer(this.WatchTimer, new TimeSpan(0, 0, 30));

            _settings = new Settings(this.ThisLock);
            
            _connectionsManager.GetLockSignaturesEvent = (object sender) =>
            {
                var signatures = new HashSet<string>();
                signatures.UnionWith(_settings.TrustSignatures);
                signatures.UnionWith(_cache_Metadata_Profile_Pairs.Keys.Select(n => n.Certificate.ToString()));
                signatures.UnionWith(_cache_Metadata_SignatureMessage_Pairs_Dictionary.Keys);

                return signatures;
            };

            _connectionsManager.GetLockWikisEvent = (object sender) =>
            {
                var wikis = new HashSet<Wiki>();
                wikis.UnionWith(_cache_Metadata_WikiDocument_Pairs_Dictionary.Keys);

                return wikis;
            };

            _connectionsManager.GetLockChatsEvent = (object sender) =>
            {
                var chats = new HashSet<Chat>();
                chats.UnionWith(_cache_Metadata_ChatMessage_Pairs_Dictionary.Keys);

                return chats;
            };
        }

        private void WatchTimer()
        {
            lock (this.ThisLock)
            {
                _cache_Metadata_Profile_Pairs.TrimExcess();
                _cache_Metadata_SignatureMessage_Pairs_Dictionary.TrimExcess();
                _cache_Metadata_WikiDocument_Pairs_Dictionary.TrimExcess();
                _cache_Metadata_ChatMessage_Pairs_Dictionary.TrimExcess();
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

        public Profile GetProfile(string signature)
        {
            lock (this.ThisLock)
            {
                Profile profile = null;

                {
                    var metadata = _connectionsManager.GetProfileMetadata(signature);
                    if (metadata == null) goto End;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    if (!_cache_Metadata_Profile_Pairs.TryGetValue(metadata, out profile))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromProfileBlock(buffer);

                            if (metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) goto End;

                            _cache_Metadata_Profile_Pairs[metadata] = package;

                            profile = package;
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

                return profile;
            }
        }

        public IEnumerable<SignatureMessage> GetSignatureMessages(string signature, ExchangePrivateKey exchangePrivateKey, int limit)
        {
            if (signature == null) throw new ArgumentNullException("signature");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                Dictionary<SignatureMessageMetadata, SignatureMessage> dic;

                if (!_cache_Metadata_SignatureMessage_Pairs_Dictionary.TryGetValue(signature, out dic))
                {
                    dic = new Dictionary<SignatureMessageMetadata, SignatureMessage>();
                    _cache_Metadata_SignatureMessage_Pairs_Dictionary[signature] = dic;
                }

                var metadatas = new HashSet<SignatureMessageMetadata>(_connectionsManager.GetSignatureMessageMetadatas(signature));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (!metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var signatureMessages = new List<SignatureMessage>();

                foreach (var metadata in metadatas)
                {
                    if (limit < 0)
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString())) continue;
                    }
                    else
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;
                    }

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    SignatureMessage signatureMessage;

                    if (!dic.TryGetValue(metadata, out signatureMessage))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromSignatureMessageBlock(buffer, exchangePrivateKey);

                            if (metadata.Signature != package.Signature
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            dic[metadata] = package;

                            signatureMessage = package;
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

                    if (signatureMessage != null) signatureMessages.Add(signatureMessage);
                }

                return signatureMessages;
            }
        }

        public IEnumerable<WikiDocument> GetWikiDocuments(Wiki tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                Dictionary<WikiDocumentMetadata, WikiDocument> dic;

                if (!_cache_Metadata_WikiDocument_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<WikiDocumentMetadata, WikiDocument>();
                    _cache_Metadata_WikiDocument_Pairs_Dictionary[tag] = dic;
                }

                var metadatas = new HashSet<WikiDocumentMetadata>(_connectionsManager.GetWikiDocumentMetadatas(tag));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (!metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var wikiDocuments = new List<WikiDocument>();

                foreach (var metadata in metadatas)
                {
                    if (limit < 0)
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString())) continue;
                    }
                    else
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;
                    }

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    WikiDocument wikiDocument;

                    if (!dic.TryGetValue(metadata, out wikiDocument))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromWikiDocumentBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            dic[metadata] = package;

                            wikiDocument = package;
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

                    if (wikiDocument != null) wikiDocuments.Add(wikiDocument);
                }

                return wikiDocuments;
            }
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                Dictionary<ChatMessageMetadata, ChatMessage> dic;

                if (!_cache_Metadata_ChatMessage_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<ChatMessageMetadata, ChatMessage>();
                    _cache_Metadata_ChatMessage_Pairs_Dictionary[tag] = dic;
                }

                var metadatas = new HashSet<ChatMessageMetadata>(_connectionsManager.GetChatMessageMetadatas(tag));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (!metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var chatMessages = new List<ChatMessage>();

                foreach (var metadata in metadatas)
                {
                    if (limit < 0)
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString())) continue;
                    }
                    else
                    {
                        if (!_settings.TrustSignatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;
                    }

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    ChatMessage chatMessage;

                    if (!dic.TryGetValue(metadata, out chatMessage))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromChatMessageBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            dic[metadata] = package;

                            chatMessage = package;
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

                    if (chatMessage != null) chatMessages.Add(chatMessage);
                }

                return chatMessages;
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
