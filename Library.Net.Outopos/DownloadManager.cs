using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Security;

namespace Library.Net.Outopos
{
    class DownloadManager : ManagerBase, IThisLock
    {
        private ConnectionsManager _connectionsManager;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha512;

        private volatile bool _disposed;
        private readonly object _thisLock = new object();

        public DownloadManager(ConnectionsManager connectionsManager, CacheManager cacheManager, BufferManager bufferManager)
        {
            _connectionsManager = connectionsManager;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
        }

        public ProfileContent GetContent(ProfileMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        return ContentConverter.FromProfileContentBlock(buffer);
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

                    return null;
                }
            }
        }

        public SignatureMessageContent GetContent(SignatureMessageMetadata metadata, ExchangePrivateKey exchangePrivateKey)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (exchangePrivateKey == null) throw new ArgumentNullException("exchangePrivateKey");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        return ContentConverter.FromSignatureMessageContentBlock(buffer, exchangePrivateKey);
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

                    return null;
                }
            }
        }

        public WikiDocumentContent GetContent(WikiDocumentMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        return ContentConverter.FromWikiDocumentContentBlock(buffer);
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

                    return null;
                }
            }
        }

        public ChatTopicContent GetContent(ChatTopicMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        return ContentConverter.FromChatTopicContentBlock(buffer);
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

                    return null;
                }
            }
        }

        public ChatMessageContent GetContent(ChatMessageMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");

            lock (this.ThisLock)
            {
                if (!_cacheManager.Contains(metadata.Key))
                {
                    _connectionsManager.Download(metadata.Key);

                    return null;
                }
                else
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _cacheManager[metadata.Key];

                        return ContentConverter.FromChatMessageContentBlock(buffer);
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

                    return null;
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
