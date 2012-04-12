﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Library.Io
{
    public delegate void GetProgressEventHandler(object sender, long readSize, long writeSize, out bool isStop);

    public class ProgressStream : Stream
    {
        private Stream _stream;
        private event GetProgressEventHandler _getProgressEvent;
        private bool _leaveInnerStreamOpen = false;
        private long _totalReadSize;
        private long _totalWriteSize;
        private long _tempReadSize;
        private long _tempWriteSize;
        private int _unit;
        private bool _disposed = false;

        public ProgressStream(Stream stream, GetProgressEventHandler getProgressEvent, int unit, bool leaveInnerStreamOpen)
        {
            _stream = stream;
            _getProgressEvent += getProgressEvent;
            _unit = unit;
            _leaveInnerStreamOpen = leaveInnerStreamOpen;
        }

        public ProgressStream(Stream stream, GetProgressEventHandler getProgressEvent, int unit)
            : this(stream, getProgressEvent, unit, false)
        {

        }

        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanRead;
            }
        }

        public override bool CanSeek
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanSeek;
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanWrite;
            }
        }

        public override long Position
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Position;
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_stream.CanSeek) throw new NotSupportedException();

                _stream.Position = value;
            }
        }

        public override long Length
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Length;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

            int readLength = _stream.Read(buffer, offset, count);
            _tempReadSize += readLength;

            while (_tempReadSize >= _unit)
            {
                _tempReadSize -= _unit;
                _totalReadSize += _unit;

                bool isStop = false;
                _getProgressEvent.Invoke(this, _totalReadSize, _totalWriteSize, out isStop);

                if (isStop)
                {
                    throw new StopIOException();
                }
            }

            return readLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

            _stream.Write(buffer, offset, count);
            _tempWriteSize += count;

            while (_tempWriteSize >= _unit)
            {
                _tempWriteSize -= _unit;
                _totalWriteSize += _unit;

                bool isStop = false;
                _getProgressEvent.Invoke(this, _totalReadSize, _totalWriteSize, out isStop);

                if (isStop)
                {
                    throw new StopIOException();
                }
            }
        }

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.Flush();
        }

        public override void Close()
        {
            if (_disposed) return;

            this.Flush();
            this.Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_disposed) return;

                if (disposing)
                {
                    if (_stream != null && !_leaveInnerStreamOpen)
                    {
                        try
                        {
                            _stream.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _stream = null;
                    }
                }

                _disposed = true;
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }

    [Serializable]
    public class StopIOException : IOException
    {
        public StopIOException() : base() { }
        public StopIOException(string message) : base(message) { }
        public StopIOException(string message, Exception innerException) : base(message, innerException) { }
    }
}
