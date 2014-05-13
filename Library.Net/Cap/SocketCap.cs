using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace Library.Net
{
    public class SocketCap : CapBase
    {
        private Socket _socket;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;
        private volatile bool _disposed;

        public SocketCap(Socket socket)
        {
            _socket = socket;
            _connect = true;
        }

        public Socket Socket
        {
            get
            {
                return _socket;
            }
        }

        public override int Receive(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException("Closed");

            try
            {
                lock (_receiveLock)
                {
                    _socket.ReceiveTimeout = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);

                    var i = _socket.Receive(buffer, offset, size, SocketFlags.None);

                    if (i == 0)
                    {
                        _connect = false;

                        throw new CapException("Closed");
                    }

                    return i;
                }
            }
            catch (CapException)
            {
                throw;
            }
            catch (Exception e)
            {
                _connect = false;

                throw new CapException("Receive", e);
            }
        }

        public override void Send(byte[] buffer, int offset, int size, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new CapException();

            try
            {
                lock (_sendLock)
                {
                    _socket.SendTimeout = (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds);

                    _socket.Send(buffer, offset, size, SocketFlags.None);
                }
            }
            catch (CapException)
            {
                throw;
            }
            catch (Exception e)
            {
                _connect = false;

                throw new CapException("Send", e);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_socket != null)
                {
                    try
                    {
                        _socket.Shutdown(SocketShutdown.Send);
                        _socket.Close();
                        _socket.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _socket = null;
                }

                _connect = false;
            }
        }
    }
}
