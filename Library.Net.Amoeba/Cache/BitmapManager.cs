﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Library.Net.Amoeba
{
    class BitmapManager : ManagerBase
    {
        private FileStream _bitmapStream;
        private BufferManager _bufferManager;

        private Settings _settings;
        private long _length;

        private bool _cacheChanged = false;
        private long _cacheSector = -1;

        private byte[] _cacheBuffer;
        private int _cacheBufferLength = 0;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public static readonly int SectorSize = 1024 * 4;

        public BitmapManager(string bitmapPath, BufferManager bufferManager)
        {
            _bitmapStream = new FileStream(bitmapPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _bufferManager = bufferManager;

            _cacheBuffer = _bufferManager.TakeBuffer(BitmapManager.SectorSize);

            _settings = new Settings(_thisLock);
        }

        private static long Roundup(long value, long unit)
        {
            if (value % unit == 0) return value;
            else return ((value / unit) + 1) * unit;
        }

        public long Length
        {
            get
            {
                lock (_thisLock)
                {
                    return _length;
                }
            }
        }

        public void SetLength(long length)
        {
            lock (_thisLock)
            {
                {
                    var size = BitmapManager.Roundup(length, 8);

                    _bitmapStream.SetLength(size);
                    _bitmapStream.Seek(0, SeekOrigin.Begin);

                    {
                        using (var safeBuffer = _bufferManager.CreateSafeBuffer(4096))
                        {
                            Unsafe.Zero(safeBuffer.Value);

                            for (long i = (size / safeBuffer.Value.Length), remain = size; i >= 0; i--, remain -= safeBuffer.Value.Length)
                            {
                                _bitmapStream.Write(safeBuffer.Value, 0, (int)Math.Min(remain, safeBuffer.Value.Length));
                                _bitmapStream.Flush();
                            }
                        }
                    }
                }

                _length = length;

                {
                    _cacheChanged = false;
                    _cacheSector = -1;

                    _cacheBufferLength = 0;
                }
            }
        }

        private void Flush()
        {
            if (_cacheChanged)
            {
                _bitmapStream.Seek(_cacheSector * BitmapManager.SectorSize, SeekOrigin.Begin);
                _bitmapStream.Write(_cacheBuffer, 0, _cacheBufferLength);
                _bitmapStream.Flush();

                _cacheChanged = false;
            }
        }

        private ArraySegment<byte> GetBuffer(long sector)
        {
            if (_cacheSector != sector)
            {
                this.Flush();

                _bitmapStream.Seek(sector * BitmapManager.SectorSize, SeekOrigin.Begin);
                _cacheBufferLength = _bitmapStream.Read(_cacheBuffer, 0, _cacheBuffer.Length);

                _cacheSector = sector;
            }

            return new ArraySegment<byte>(_cacheBuffer, 0, _cacheBufferLength);
        }

        public bool Get(long point)
        {
            lock (_thisLock)
            {
                if (point >= _length) throw new ArgumentOutOfRangeException(nameof(point));

                var sectorOffset = (point / 8) / BitmapManager.SectorSize;
                var bufferOffset = (int)((point / 8) % BitmapManager.SectorSize);
                var bitOffset = (byte)(point % 8);

                var buffer = this.GetBuffer(sectorOffset);
                return ((buffer.Array[buffer.Offset + bufferOffset] << bitOffset) & 0x80) == 0x80;
            }
        }

        public void Set(long point, bool state)
        {
            lock (_thisLock)
            {
                if (point >= _length) throw new ArgumentOutOfRangeException(nameof(point));

                var sectorOffset = (point / 8) / BitmapManager.SectorSize;
                var bufferOffset = (int)((point / 8) % BitmapManager.SectorSize);
                var bitOffset = (byte)(point % 8);

                if (state)
                {
                    var buffer = this.GetBuffer(sectorOffset);
                    buffer.Array[buffer.Offset + bufferOffset] |= (byte)(0x80 >> bitOffset);
                }
                else
                {
                    var buffer = this.GetBuffer(sectorOffset);
                    buffer.Array[buffer.Offset + bufferOffset] &= (byte)(~(0x80 >> bitOffset));
                }

                _cacheChanged = true;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Load(directoryPath);
                _length = _settings.Length;
            }
        }

        public void Save(string directoryPath)
        {
            lock (_thisLock)
            {
                _settings.Length = _length;
                _settings.Save(directoryPath);

                this.Flush();
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<long>() { Name = "Length", Value = 0 },
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

            public long Length
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (long)this["Length"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["Length"] = value;
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
                if (_bitmapStream != null)
                {
                    try
                    {
                        _bitmapStream.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _bitmapStream = null;
                }

                if (_cacheBuffer != null)
                {
                    try
                    {
                        _bufferManager.ReturnBuffer(_cacheBuffer);
                    }
                    catch (Exception)
                    {

                    }

                    _cacheBuffer = null;
                }
            }
        }
    }
}
