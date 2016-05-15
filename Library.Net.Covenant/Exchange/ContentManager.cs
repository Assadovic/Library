using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Covenant
{
    class ContentManager : ManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private BufferManager _bufferManager;

        private Settings _settings;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const HashAlgorithm _hashAlgorithm = HashAlgorithm.Sha256;
        private const int _hashSize = 32;

        public ContentManager(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);
        }

        public Task<Key> Import(string path, CancellationToken token)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            lock (this.ThisLock)
            {
                if (_settings.ContentItems.Values.Any(n => n.Path == path)) return null;

                return Task.Run(() =>
                {
                    Key key;
                    Bitmap bitmap;
                    BlocksInfo blocksInfo;

                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int blockLength = 1024 * 1024;

                        int count;
                        {
                            long i = 0;
                            while ((i = (stream.Length + (blockLength - 1)) / blockLength) > Bitmap.MaxLength) blockLength *= 2;
                            count = (int)i;
                        }

                        var hashes = new byte[count * _hashSize];

                        using (var safeBuffer = _bufferManager.CreateSafeBuffer(blockLength))
                        {
                            for (int i = 0; stream.Position < stream.Length; i++)
                            {
                                token.ThrowIfCancellationRequested();

                                var length = (int)Math.Min(stream.Length - stream.Position, blockLength);
                                stream.Read(safeBuffer.Value, 0, length);

                                var hash = Sha256.ComputeHash(safeBuffer.Value, 0, length);
                                Unsafe.Copy(hash, 0, hashes, i * _hashSize, _hashSize);
                            }
                        }

                        blocksInfo = new BlocksInfo(blockLength, _hashAlgorithm, hashes);
                        bitmap = new Bitmap(count);
                        {
                            for (int i = 0; i < count; i++)
                            {
                                bitmap.Set(i, true);
                            }
                        }
                        key = new Key(_hashAlgorithm, blocksInfo.CreateHash(_hashAlgorithm));
                    }

                    var options = new ContentOptions();
                    options.Bitmap = bitmap;
                    options.BlocksInfo = blocksInfo;
                    options.Path = path;

                    _settings.ContentItems.Add(key, options);

                    return key;
                }, token);
            }
        }

        public void Export(Key key, BlocksInfo blocksInfo, string path)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (blocksInfo == null) throw new ArgumentNullException(nameof(blocksInfo));
            if (path == null) throw new ArgumentNullException(nameof(path));

            lock (this.ThisLock)
            {
                if (_settings.ContentItems.Values.Any(n => n.Path == path)) throw new ContentManagerException();
                if (!Unsafe.Equals(key.Hash, blocksInfo.CreateHash(key.HashAlgorithm))) throw new ContentManagerException();

                var options = new ContentOptions();
                options.Bitmap = new Bitmap(blocksInfo.Count);
                options.BlocksInfo = blocksInfo;
                options.Path = path;

                _settings.ContentItems.Add(key, options);
            }
        }

        public bool Contains(Key key)
        {
            lock (this.ThisLock)
            {
                return _settings.ContentItems.ContainsKey(key);
            }
        }

        public void Remove(Key key)
        {
            lock (this.ThisLock)
            {
                _settings.ContentItems.Remove(key);
            }
        }

        public byte[] GetBitmap(Key key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (this.ThisLock)
            {
                ContentOptions options;
                if (!_settings.ContentItems.TryGetValue(key, out options)) throw new KeyNotFoundException();

                return options.Bitmap.ToBinary();
            }
        }

        public BlocksInfo GetBlocksInfo(Key key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (this.ThisLock)
            {
                ContentOptions options;
                if (!_settings.ContentItems.TryGetValue(key, out options)) throw new KeyNotFoundException();

                return options.BlocksInfo;
            }
        }

        public ArraySegment<byte> GetBlock(Key key, int index)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (this.ThisLock)
            {
                ContentOptions options;
                if (!_settings.ContentItems.TryGetValue(key, out options)) throw new KeyNotFoundException();

                if (index < 0 || index >= options.BlocksInfo.Count) throw new ArgumentOutOfRangeException();
                if (!options.Bitmap.Get(index)) throw new BlockNotFoundException();

                byte[] buffer = _bufferManager.TakeBuffer(options.BlocksInfo.BlockLength);

                try
                {
                    int length;

                    try
                    {
                        using (var stream = new FileStream(options.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            stream.Seek((long)index * options.BlocksInfo.BlockLength, SeekOrigin.Begin);

                            length = (int)Math.Min(stream.Length - stream.Position, options.BlocksInfo.BlockLength);
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

                    if (options.BlocksInfo.HashAlgorithm == HashAlgorithm.Sha256)
                    {
                        if (!Unsafe.Equals(Sha256.ComputeHash(buffer, 0, length), options.BlocksInfo.Get(index)))
                        {
                            options.Bitmap.Set(index, false);

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

        public void SetBlock(Key key, int index, ArraySegment<byte> buffer)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (this.ThisLock)
            {
                ContentOptions options;
                if (!_settings.ContentItems.TryGetValue(key, out options)) throw new KeyNotFoundException();

                if (index < 0 || index >= options.BlocksInfo.Count) throw new ArgumentOutOfRangeException();
                if (!(index < (options.BlocksInfo.Count - 1) && buffer.Count == options.BlocksInfo.BlockLength
                    || index == (options.BlocksInfo.Count - 1) && buffer.Count <= options.BlocksInfo.BlockLength)) throw new ArgumentOutOfRangeException();

                if (options.BlocksInfo.HashAlgorithm == HashAlgorithm.Sha256)
                {
                    if (!Unsafe.Equals(Sha256.ComputeHash(buffer), options.BlocksInfo.Get(index)))
                    {
                        throw new BadBlockException();
                    }
                }
                else
                {
                    throw new FormatException();
                }

                try
                {
                    using (var stream = new FileStream(options.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        stream.Seek((long)index * options.BlocksInfo.BlockLength, SeekOrigin.Begin);

                        stream.Write(buffer.Array, buffer.Offset, buffer.Count);
                        stream.Flush();
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new ContentManagerException();
                }
                catch (IOException)
                {
                    throw new ContentManagerException();
                }

                options.Bitmap.Set(index, true);
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
                    new Library.Configuration.SettingContent<LockedHashDictionary<Key, ContentOptions>>() { Name = "ContentItems", Value = new LockedHashDictionary<Key, ContentOptions>() },
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
                    ;
                    base.Save(directoryPath);
                }
            }

            public LockedHashDictionary<Key, ContentOptions> ContentItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedHashDictionary<Key, ContentOptions>)this["ContentItems"];
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
    class ContentManagerException : ManagerException
    {
        public ContentManagerException() : base() { }
        public ContentManagerException(string message) : base(message) { }
        public ContentManagerException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BlockNotFoundException : ContentManagerException
    {
        public BlockNotFoundException() : base() { }
        public BlockNotFoundException(string message) : base(message) { }
        public BlockNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }

    [Serializable]
    class BadBlockException : ContentManagerException
    {
        public BadBlockException() : base() { }
        public BadBlockException(string message) : base(message) { }
        public BadBlockException(string message, Exception innerException) : base(message, innerException) { }
    }
}