﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Library.Io;

namespace Library.Net.Connection
{
    public class CrcConnection : ConnectionBase, IThisLock
    {
        private ConnectionBase _connection;
        private BufferManager _bufferManager;

        private bool _disposed = false;
        
        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        public CrcConnection(ConnectionBase connection, BufferManager bufferManager)
        {
            _connection = connection;
            _bufferManager = bufferManager;
        }

        public override long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public override long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public ConnectionBase Connection
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connection;
                }
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {

            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Close(timeout);
                        _connection.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }
            }
        }

        public override System.IO.Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(_receiveLock))
            {
                Stream stream = null;
                RangeStream dataStream = null;

                try
                {
                    stream = _connection.Receive(timeout);

                    dataStream = new RangeStream(stream, 0, stream.Length - 4);
                    byte[] verifyCrc = Crc32_Castagnoli.ComputeHash(dataStream);
                    byte[] orignalCrc = new byte[4];

                    using (RangeStream crcStream = new RangeStream(stream, stream.Length - 4, 4, true))
                    {
                        crcStream.Read(orignalCrc, 0, orignalCrc.Length);
                    }

                    if (!Collection.Equals(verifyCrc, orignalCrc)) throw new ArgumentException("Crc Error");

                    dataStream.Seek(0, SeekOrigin.Begin);
                    return dataStream;
                }
                catch (ConnectionException ex)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

                    throw ex;
                }
                catch (Exception e)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        public override void Send(System.IO.Stream stream, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            using (DeadlockMonitor.Lock(_sendLock))
            {
                try
                {
                    using (MemoryStream crcStream = new MemoryStream(Crc32_Castagnoli.ComputeHash(stream)))
                    using (Stream stream2 = new AddStream(new RangeStream(stream, true), crcStream))
                    {
                        _connection.Send(stream2, timeout);
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception e)
                {
                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_connection != null)
                    {
                        try
                        {
                            _connection.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _connection = null;
                    }
                }

                _disposed = true;
            }
        }

        #region IThisLock メンバ

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
    public class CrcErrorException : ConnectionException
    {
        public CrcErrorException() : base() { }
        public CrcErrorException(string message) : base(message) { }
        public CrcErrorException(string message, Exception innerException) : base(message, innerException) { }
    }
}
