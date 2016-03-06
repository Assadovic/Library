using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Library;

namespace Library.Net.I2p
{
    public class SamManager : ManagerBase
    {
        private string _host;
        private int _port;
        private string _caption;

        private SamSession _samSession;

        private volatile bool _disposed;

        public SamManager(string host, int port, string caption)
        {
            _host = host;
            _port = port;
            _caption = caption;
        }

        private Socket GetSocket()
        {
            Socket socket;

            using (var tcpClient = new TcpClient(_host, _port))
            {
                socket = tcpClient.Client;

                tcpClient.Client = null;
            }

            socket.SendTimeout = 60 * 1000;
            socket.ReceiveTimeout = 60 * 1000;

            return socket;
        }

        public string Start()
        {
            {
                if (_samSession != null)
                {
                    _samSession.Socket.Dispose();
                    _samSession = null;
                }
            }

            {
                Socket socket = null;

                try
                {
                    socket = this.GetSocket();

                    _samSession = new SamSession(socket);
                    _samSession.Start(_caption);
                }
                catch (Exception)
                {
                    if (socket != null) socket.Dispose();
                }
            }

            return I2pConverter.Base32Address.FromDestinationBase64(_samSession.DestinationBase64);
        }

        public Socket Connect(string destination)
        {
            if (_samSession == null || !_samSession.IsConnected) return null;

            Socket socket = null;

            try
            {
                socket = this.GetSocket();

                SamConnect samConnect = new SamConnect(socket);
                samConnect.Start(_samSession.SessionId, destination);

                return samConnect.Socket;
            }
            catch (Exception)
            {
                if (socket != null) socket.Dispose();
            }

            return null;
        }

        public Socket Accept(out string destination)
        {
            destination = null;
            if (_samSession == null || !_samSession.IsConnected) return null;

            Socket socket = null;

            try
            {
                socket = this.GetSocket();

                SamAccept samAccept = new SamAccept(socket);
                samAccept.Start(_samSession.SessionId);

                destination = I2pConverter.Base32Address.FromDestinationBase64(samAccept.DestinationBase64);

                return samAccept.Socket;
            }
            catch (Exception)
            {
                if (socket != null) socket.Dispose();
            }

            return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _samSession.Socket.Dispose();
                _samSession = null;
            }
        }
    }

    class SamSession : SamBase
    {
        public SamSession(Socket socket)
            : base(socket)
        {

        }

        public string SessionId { get; private set; }
        public string DestinationBase64 { get; private set; }
        public string PrivateBase64 { get; private set; }

        public void Start(string caption)
        {
            this.Handshake();

            {
                byte[] buffer = new byte[32];

                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(buffer);
                }

                this.SessionId = NetworkConverter.ToBase64UrlString(buffer);
            }

            this.PrivateBase64 = this.SessionCreate(this.SessionId, caption);
            this.DestinationBase64 = this.NamingLookup("ME");
        }
    }

    class SamConnect : SamBase
    {
        public SamConnect(Socket socket)
            : base(socket)
        {

        }

        public string DestinationBase64 { get; private set; }

        public void Start(string sessionId, string destination)
        {
            this.Handshake();

            string destinationBase64 = this.NamingLookup(destination);
            this.StreamConnect(sessionId, destinationBase64);
            this.DestinationBase64 = destinationBase64;
        }
    }

    class SamAccept : SamBase
    {
        public SamAccept(Socket socket)
            : base(socket)
        {

        }

        public string DestinationBase64 { get; private set; }

        public void Start(string sessionId)
        {
            this.Handshake();

            this.DestinationBase64 = this.StreamAccept(sessionId);
        }
    }

    public class SamException : Exception
    {
        public SamException() : base() { }
        public SamException(string message) : base(message) { }
        public SamException(string message, Exception innerException) : base(message, innerException) { }
    }
}
