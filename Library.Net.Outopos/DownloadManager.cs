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

            _settings = new Settings(this.ThisLock);

            _connectionsManager.GetLockSignaturesEvent = (object sender) =>
            {
                return this.SearchSignatures;
            };

            _connectionsManager.GetLockWikisEvent = (object sender) =>
            {
                return this.SearchWikis;
            };

            _connectionsManager.GetLockChatsEvent = (object sender) =>
            {
                return this.SearchChats;
            };
        }

        public IEnumerable<string> SearchSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Signatures.ToArray();
                }
            }
        }

        public IEnumerable<Wiki> SearchWikis
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Wikis.ToArray();
                }
            }
        }

        public IEnumerable<Chat> SearchChats
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.Chats.ToArray();
                }
            }
        }

        public void SetSearchSignatures(IEnumerable<string> signatures)
        {
            lock (this.ThisLock)
            {
                lock (_settings.Signatures.ThisLock)
                {
                    _settings.Signatures.Clear();
                    _settings.Signatures.UnionWith(new SignatureCollection(signatures));
                }
            }
        }

        public void SetSearchWikis(IEnumerable<Wiki> wikis)
        {
            lock (this.ThisLock)
            {
                lock (_settings.Wikis.ThisLock)
                {
                    _settings.Wikis.Clear();
                    _settings.Wikis.UnionWith(new WikiCollection(wikis));
                }
            }
        }

        public void SetSearchChats(IEnumerable<Chat> chats)
        {
            lock (this.ThisLock)
            {
                lock (_settings.Chats.ThisLock)
                {
                    _settings.Chats.Clear();
                    _settings.Chats.UnionWith(new ChatCollection(chats));
                }
            }
        }

        public IEnumerable<Profile> GetProfiles()
        {
            lock (this.ThisLock)
            {
                var profiles = new Dictionary<string, Profile>();

                foreach (var signature in this.SearchSignatures)
                {
                    var metadata = _connectionsManager.GetProfileMetadata(signature);
                    if (metadata == null) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromProfileBlock(buffer);

                            if (metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            profiles[signature] = package;
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

                foreach (var signature in this.SearchSignatures)
                {
                    if (!profiles.ContainsKey(signature) && _settings.Profiles.ContainsKey(signature))
                    {
                        profiles[signature] = _settings.Profiles[signature];
                    }
                }

                _settings.Profiles.Clear();

                foreach (var pair in profiles)
                {
                    _settings.Profiles.Add(pair.Key, pair.Value);
                }

                return _settings.Profiles.Values;
            }
        }

        public IEnumerable<SignatureMessage> GetSignatureMessages(string signature, ExchangePrivateKey exchangePrivateKey, int limit)
        {
            if (signature == null) throw new ArgumentNullException("signature");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                if (!_settings.Signatures.Contains(signature)) return new SignatureMessage[0];

                var signatureMessages = new List<SignatureMessage>();

                foreach (var metadata in _connectionsManager.GetSignatureMessageMetadatas(signature))
                {
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromSignatureMessageBlock(buffer, exchangePrivateKey);

                            if (metadata.Signature != package.Signature
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            signatureMessages.Add(package);
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

                return signatureMessages;
            }
        }

        public IEnumerable<WikiDocument> GetWikiDocuments(Wiki tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                if (!_settings.Wikis.Contains(tag)) return new WikiDocument[0];

                var wikiDocuments = new List<WikiDocument>();

                foreach (var metadata in _connectionsManager.GetWikiDocumentMetadatas(tag))
                {
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromWikiDocumentBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            wikiDocuments.Add(package);
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

                return wikiDocuments;
            }
        }

        public IEnumerable<ChatTopic> GetChatTopics(Chat tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                if (!_settings.Chats.Contains(tag)) return new ChatTopic[0];

                var chatTopics = new List<ChatTopic>();

                foreach (var metadata in _connectionsManager.GetChatTopicMetadatas(tag))
                {
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromChatTopicBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            chatTopics.Add(package);
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

                return chatTopics;
            }
        }

        public IEnumerable<ChatMessage> GetChatMessages(Chat tag, int limit)
        {
            if (tag == null) throw new ArgumentNullException("tag");

            lock (this.ThisLock)
            {
                if (!_settings.Chats.Contains(tag)) return new ChatMessage[0];

                var chatMessages = new List<ChatMessage>();

                foreach (var metadata in _connectionsManager.GetChatMessageMetadatas(tag))
                {
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString()) && metadata.Cost < limit) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }
                    else
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromChatMessageBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            chatMessages.Add(package);
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
                    new Library.Configuration.SettingContent<LockedHashSet<string>>() { Name = "Signatures", Value = new LockedHashSet<string>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Wiki>>() { Name = "Wikis", Value = new LockedHashSet<Wiki>() },
                    new Library.Configuration.SettingContent<LockedHashSet<Chat>>() { Name = "Chats", Value = new LockedHashSet<Chat>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<string, Profile>>() { Name = "Profiles", Value = new LockedHashDictionary<string, Profile>() },
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

            public LockedHashSet<Wiki> Wikis
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Wiki>)this["Wikis"];
                    }
                }
            }

            public LockedHashSet<Chat> Chats
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashSet<Chat>)this["Chats"];
                    }
                }
            }

            public LockedHashDictionary<string, Profile> Profiles
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<string, Profile>)this["Profiles"];
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
