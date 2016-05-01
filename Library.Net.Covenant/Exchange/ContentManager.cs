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

        private Stream GetStream(string path)
        {
            lock (this.ThisLock)
            {
                Stream stream;

                if (!_streams.TryGetValue(path, out stream))
                {
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _streams.Add(path, stream);
                }

                return stream;
            }
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
                    Index index;

                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int unit = 1024 * 1024;

                        int count;
                        {
                            long i = 0;
                            while ((i = (stream.Length + (unit - 1)) / unit) > Bitmap.MaxLength) unit *= 2;
                            count = (int)i;
                        }

                        var map = new byte[count * _hashSize];

                        using (var safeBuffer = _bufferManager.CreateSafeBuffer(unit))
                        {
                            for (int i = 0; stream.Position < stream.Length; i++)
                            {
                                token.ThrowIfCancellationRequested();

                                var length = (int)Math.Min(stream.Length - stream.Position, unit);
                                stream.Read(safeBuffer.Value, 0, length);

                                var hash = Sha256.ComputeHash(safeBuffer.Value, 0, length);
                                Unsafe.Copy(hash, 0, map, i * _hashSize, _hashSize);
                            }
                        }

                        index = new Index(unit, _hashAlgorithm, map);
                        bitmap = new Bitmap(count);
                        {
                            for (int i = 0; i < count; i++)
                            {
                                bitmap.Set(i, true);
                            }
                        }
                        key = new Key(_hashAlgorithm, index.CreateHash(_hashAlgorithm));
                    }

                    var options = new ContentOptions();
                    options.Bitmap = bitmap;
                    options.Index = index;
                    options.Path = path;

                    _settings.ContentItems.Add(key, options);

                    return key;
                }, token);
            }
        }

        public void Export(Key key, Index index, string path)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (path == null) throw new ArgumentNullException(nameof(path));

            lock (this.ThisLock)
            {
                if (_settings.ContentItems.Values.Any(n => n.Path == path)) throw new ContentManagerException();
                if (!Unsafe.Equals(key.Hash, index.CreateHash(key.HashAlgorithm))) throw new ContentManagerException();

                var options = new ContentOptions();
                options.Bitmap = new Bitmap(index.Count);
                options.Index = index;
                options.Path = path;

                _settings.ContentItems.Add(key, options);
            }
        }

        public void Remove(Key key)
        {
            lock (this.ThisLock)
            {
                _settings.ContentItems.Remove(key);
            }
        }

        public ArraySegment<byte> Get(Key key, int index)
        {
            lock (this.ThisLock)
            {
                ContentOptions options;
                if (!_settings.ContentItems.TryGetValue(key, out options)) throw new KeyNotFoundException();

                if (index >= options.Index.Count) throw new ArgumentOutOfRangeException();
                if (!options.Bitmap.Get(index)) throw new BlockNotFoundException();

                using (var stream = new FileStream(options.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {

                }
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);

                foreach (var key in _settings.ContentItems.Keys)
                {

                }
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

}
