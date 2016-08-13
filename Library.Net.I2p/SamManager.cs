using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Library;

namespace Library.Net.I2p
{
    public class SamManager : ManagerBase
    {
        private string _host;
        private int _port;
        private string _caption;

        private Socket _sessionSocket;
        private string _sessionId;

        private Thread _readerThread;
        private Thread _writerThread;

        private Thread _acceptThread;
        private ConcurrentQueue<AcceptResult> _acceptResultQueue = new ConcurrentQueue<AcceptResult>();
        private CancellationTokenSource _acceptTokenSource = new CancellationTokenSource();

        private volatile bool _disposed;

        public SamManager(string host, int port, string caption)
        {
            _host = host;
            _port = port;
            _caption = caption;

            _readerThread = new Thread(this.ReaderThread);
            _readerThread.Priority = ThreadPriority.BelowNormal;
            _readerThread.Name = "SamSession_ReaderThread";

            _writerThread = new Thread(this.WriterThread);
            _writerThread.Priority = ThreadPriority.BelowNormal;
            _writerThread.Name = "SamSession_WriterThread";

            _acceptThread = new Thread(this.AcceptThread);
            _acceptThread.Priority = ThreadPriority.BelowNormal;
            _acceptThread.Name = "SamSession_AcceptThread";
        }

        private string _lastPingMessage;

        private void ReaderThread()
        {
            StreamReader reader = null;
            StreamWriter writer = null;

            try
            {
                {
                    var stream = new NetworkStream(_sessionSocket);
                    stream.ReadTimeout = 60 * 1000;
                    stream.WriteTimeout = 60 * 1000;

                    reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024 * 32);
                    writer = new StreamWriter(stream, new UTF8Encoding(false), 1024 * 32);
                    writer.NewLine = "\n";
                }

                while (this.IsConnected)
                {
                    Thread.Sleep(1000);

                    string line = reader.ReadLine();
                    if (line == null) break;

                    if (line.StartsWith("PING"))
                    {
                        writer.WriteLine(string.Format("PONG {0}", line.Substring(5)));
                        writer.Flush();
                    }
                    else if (line.StartsWith("PONG"))
                    {
                        if (_lastPingMessage != line.Substring(5))
                        {
                            break;
                        }

                        _lastPingMessage = null;
                    }
                }
            }
            catch (Exception)
            {

            }
            finally
            {
                if (reader != null) reader.Dispose();
                if (writer != null) writer.Dispose();
            }
        }

        private void WriterThread()
        {
            StreamReader reader = null;
            StreamWriter writer = null;

            try
            {
                {
                    var stream = new NetworkStream(_sessionSocket);
                    stream.ReadTimeout = 60 * 1000;
                    stream.WriteTimeout = 60 * 1000;

                    reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024 * 32);
                    writer = new StreamWriter(stream, new UTF8Encoding(false), 1024 * 32);
                    writer.NewLine = "\n";
                }

                var sw = new Stopwatch();
                sw.Start();

                while (this.IsConnected)
                {
                    Thread.Sleep(1000);
                    if (sw.Elapsed.TotalSeconds < 30) continue;

                    string text = null;

                    {
                        byte[] buffer = new byte[32];

                        using (var random = RandomNumberGenerator.Create())
                        {
                            random.GetBytes(buffer);
                        }

                        text = NetworkConverter.ToBase64UrlString(buffer);
                    }

                    _lastPingMessage = text;

                    writer.WriteLine(string.Format("PING {0}", text));
                    writer.Flush();

                    sw.Restart();
                }
            }
            catch (Exception)
            {

            }
            finally
            {
                if (reader != null) reader.Dispose();
                if (writer != null) writer.Dispose();
            }
        }

        private void AcceptThread()
        {
            try
            {
                while (this.IsConnected)
                {
                    Thread.Sleep(1000);
                    if (_acceptResultQueue.Count >= 3) continue;

                    Socket socket = null;

                    try
                    {
                        socket = this.GetSocket();

                        using (_acceptTokenSource.Token.Register(() => socket.Dispose()))
                        {
                            var samAccept = new SamAccept(socket);
                            samAccept.Start(_sessionId);

                            var destination = I2pConverter.Base32Address.FromDestinationBase64(samAccept.DestinationBase64);

                            _acceptResultQueue.Enqueue(new AcceptResult(samAccept.GetSocket(), destination));
                        }
                    }
                    catch (Exception)
                    {
                        if (socket != null) socket.Dispose();
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        public bool IsConnected { get { return (_sessionSocket != null && _sessionSocket.Connected); } }

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
            string address = null;

            {
                Socket socket = null;

                try
                {
                    socket = this.GetSocket();

                    using (CancellationTokenSource tokenSource = new CancellationTokenSource())
                    {
                        tokenSource.CancelAfter(60 * 1000);

                        using (tokenSource.Token.Register(() => socket.Dispose()))
                        {
                            var samSession = new SamSession(socket);
                            samSession.Start(_caption);

                            _sessionSocket = samSession.GetSocket();
                            _sessionId = samSession.SessionId;

                            address = I2pConverter.Base32Address.FromDestinationBase64(samSession.DestinationBase64);
                        }
                    }

                    _readerThread.Start();
                    _writerThread.Start();
                    _acceptThread.Start();
                }
                catch (Exception)
                {
                    if (socket != null) socket.Dispose();
                }
            }

            return address;
        }

        public Socket Connect(string destination)
        {
            if (!this.IsConnected) throw new SamException();

            Socket socket = null;

            try
            {
                socket = this.GetSocket();

                using (CancellationTokenSource tokenSource = new CancellationTokenSource())
                {
                    tokenSource.CancelAfter(10 * 1000);

                    using (tokenSource.Token.Register(() => socket.Dispose()))
                    {
                        var samConnect = new SamConnect(socket);
                        samConnect.Start(_sessionId, destination);

                        return samConnect.GetSocket();
                    }
                }
            }
            catch (Exception)
            {
                if (socket != null) socket.Dispose();
            }

            throw new SamException();
        }

        public Socket Accept(out string destination)
        {
            destination = null;
            if (!this.IsConnected) throw new SamException();

            try
            {
                for (;;)
                {
                    AcceptResult result;
                    if (!_acceptResultQueue.TryDequeue(out result)) break;

                    if (!result.Socket.Connected)
                    {
                        result.Socket.Dispose();

                        continue;
                    }

                    destination = result.Destination;
                    return result.Socket;
                }
            }
            catch (Exception)
            {

            }

            throw new SamException();
        }

        private class AcceptResult
        {
            public AcceptResult(Socket socket, string destination)
            {
                this.Socket = socket;
                this.Destination = destination;
            }

            public Socket Socket { get; private set; }
            public string Destination { get; private set; }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_sessionSocket != null) _sessionSocket.Dispose();
                _sessionSocket = null;

                if (_readerThread != null && _readerThread.IsAlive) _readerThread.Join();
                _readerThread = null;

                if (_writerThread != null && _writerThread.IsAlive) _writerThread.Join();
                _writerThread = null;

                _acceptTokenSource.Cancel();
                _acceptTokenSource.Dispose();
                _acceptTokenSource = null;

                if (_acceptThread != null && _acceptThread.IsAlive) _acceptThread.Join();
                _acceptThread = null;
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
