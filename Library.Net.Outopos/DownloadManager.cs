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

                // Profile
                {
                    foreach (var metadata in _settings.Metadata_Profile_Pairs.Keys.ToArray())
                    {
                        if (_settings.Signatures.Contains(metadata.Certificate.ToString())) continue;

                        _settings.Metadata_Profile_Pairs.Remove(metadata);
                    }
                }

                // SignatureMessage
                {
                    foreach (var signature in _settings.Metadata_SignatureMessage_Pairs_Dictionary.Keys.ToArray())
                    {
                        if (_settings.Signatures.Contains(signature)) continue;

                        _settings.Metadata_SignatureMessage_Pairs_Dictionary.Remove(signature);
                    }
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

                // WikiDocument
                {
                    foreach (var tag in _settings.Metadata_WikiDocument_Pairs_Dictionary.Keys.ToArray())
                    {
                        if (_settings.Wikis.Contains(tag)) continue;

                        _settings.Metadata_WikiDocument_Pairs_Dictionary.Remove(tag);
                    }
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

                // ChatTopic
                {
                    foreach (var tag in _settings.Metadata_ChatTopic_Pairs_Dictionary.Keys.ToArray())
                    {
                        if (_settings.Chats.Contains(tag)) continue;

                        _settings.Metadata_ChatTopic_Pairs_Dictionary.Remove(tag);
                    }
                }

                // ChatMessage
                {
                    foreach (var tag in _settings.Metadata_ChatMessage_Pairs_Dictionary.Keys.ToArray())
                    {
                        if (_settings.Chats.Contains(tag)) continue;

                        _settings.Metadata_ChatMessage_Pairs_Dictionary.Remove(tag);
                    }
                }
            }
        }

        public Profile GetProfile(string signature)
        {
            lock (this.ThisLock)
            {
                if (!_settings.Signatures.Contains(signature)) return null;

                Profile profile = null;

                {
                    var metadata = _connectionsManager.GetProfileMetadata(signature);
                    if (metadata == null) goto End;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    if (!_settings.Metadata_Profile_Pairs.TryGetValue(metadata, out profile))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromProfileBlock(buffer);

                            if (metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) goto End;

                            _settings.Metadata_Profile_Pairs[metadata] = package;

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
                if (!_settings.Signatures.Contains(signature)) return new SignatureMessage[0];

                Dictionary<SignatureMessageMetadata, SignatureMessage> dic;

                if (!_settings.Metadata_SignatureMessage_Pairs_Dictionary.TryGetValue(signature, out dic))
                {
                    dic = new Dictionary<SignatureMessageMetadata, SignatureMessage>();
                    _settings.Metadata_SignatureMessage_Pairs_Dictionary[signature] = dic;
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
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString())
                        && (limit != -1 && metadata.Cost < limit)) continue;

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

                Dictionary<WikiDocumentMetadata, WikiDocument> dic;

                if (!_settings.Metadata_WikiDocument_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<WikiDocumentMetadata, WikiDocument>();
                    _settings.Metadata_WikiDocument_Pairs_Dictionary[tag] = dic;
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
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString())
                        && (limit != -1 && metadata.Cost < limit)) continue;

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

                Dictionary<ChatTopicMetadata, ChatTopic> dic;

                if (!_settings.Metadata_ChatTopic_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<ChatTopicMetadata, ChatTopic>();
                    _settings.Metadata_ChatTopic_Pairs_Dictionary[tag] = dic;
                }

                var metadatas = new HashSet<ChatTopicMetadata>(_connectionsManager.GetChatTopicMetadatas(tag));

                foreach (var metadata in dic.Keys.ToArray())
                {
                    if (!metadatas.Contains(metadata)) continue;

                    dic.Remove(metadata);
                }

                var chatTopics = new List<ChatTopic>();

                foreach (var metadata in metadatas)
                {
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString())
                        && (limit != -1 && metadata.Cost < limit)) continue;

                    if (!_cacheManager.Contains(metadata.Key))
                    {
                        _connectionsManager.Download(metadata.Key);
                    }

                    ChatTopic chatTopic;

                    if (!dic.TryGetValue(metadata, out chatTopic))
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>();

                        try
                        {
                            buffer = _cacheManager[metadata.Key];

                            var package = ContentConverter.FromChatTopicBlock(buffer);

                            if (metadata.Tag != package.Tag
                                || metadata.CreationTime != package.CreationTime
                                || metadata.Certificate.ToString() != package.Certificate.ToString()) continue;

                            dic[metadata] = package;

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

                Dictionary<ChatMessageMetadata, ChatMessage> dic;

                if (!_settings.Metadata_ChatMessage_Pairs_Dictionary.TryGetValue(tag, out dic))
                {
                    dic = new Dictionary<ChatMessageMetadata, ChatMessage>();
                    _settings.Metadata_ChatMessage_Pairs_Dictionary[tag] = dic;
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
                    if (!_settings.Signatures.Contains(metadata.Certificate.ToString())
                        && (limit != -1 && metadata.Cost < limit)) continue;

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
                    new Library.Configuration.SettingContent<LockedHashDictionary<ProfileMetadata, Profile>>() { Name = "Metadata_Profile_Pairs", Value = new LockedHashDictionary<ProfileMetadata, Profile>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>>>() { Name = "Metadata_SignatureMessage_Pairs_Dictionary", Value = new LockedHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>>>() { Name = "Metadata_WikiDocument_Pairs_Dictionary", Value = new LockedHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<Chat, Dictionary<ChatTopicMetadata, ChatTopic>>>() { Name = "Metadata_ChatTopic_Pairs_Dictionary", Value = new LockedHashDictionary<Chat, Dictionary<ChatTopicMetadata, ChatTopic>>() },
                    new Library.Configuration.SettingContent<LockedHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>>>() { Name = "Metadata_ChatMessage_Pairs_Dictionary", Value = new LockedHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>>() },
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

            public LockedHashDictionary<ProfileMetadata, Profile> Metadata_Profile_Pairs
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<ProfileMetadata, Profile>)this["Metadata_Profile_Pairs"];
                    }
                }
            }

            public LockedHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>> Metadata_SignatureMessage_Pairs_Dictionary
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<string, Dictionary<SignatureMessageMetadata, SignatureMessage>>)this["Metadata_SignatureMessage_Pairs_Dictionary"];
                    }
                }
            }

            public LockedHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>> Metadata_WikiDocument_Pairs_Dictionary
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Wiki, Dictionary<WikiDocumentMetadata, WikiDocument>>)this["Metadata_WikiDocument_Pairs_Dictionary"];
                    }
                }
            }

            public LockedHashDictionary<Chat, Dictionary<ChatTopicMetadata, ChatTopic>> Metadata_ChatTopic_Pairs_Dictionary
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Chat, Dictionary<ChatTopicMetadata, ChatTopic>>)this["Metadata_ChatTopic_Pairs_Dictionary"];
                    }
                }
            }

            public LockedHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>> Metadata_ChatMessage_Pairs_Dictionary
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Chat, Dictionary<ChatMessageMetadata, ChatMessage>>)this["Metadata_ChatMessage_Pairs_Dictionary"];
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
