﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Library.Net;
using Library.Net.Connections;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Net.Connection")]
    public class Test_Library_Net_Connection
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        private const int MaxReceiveCount = 1 * 1024 * 1024;

        [Test]
        public void Test_BaseConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            using (var baseClient = new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            using (var baseServer = new BaseConnection(new SocketCap(server), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var clientSendTask = baseClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var serverReceiveTask = baseServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(clientSendTask, serverReceiveTask);

                    using (var returnStream = serverReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "BaseConnection #1");
                    }
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var serverSendTask = baseServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var clientReceiveTask = baseClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(serverSendTask, clientReceiveTask);

                    using (var returnStream = clientReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "BaseConnection #2");
                    }
                }
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_CrcConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            using (var crcClient = new CrcConnection(new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), _bufferManager))
            using (var crcServer = new CrcConnection(new BaseConnection(new SocketCap(server), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), _bufferManager))
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var clientSendTask = crcClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var serverReceiveTask = crcServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(clientSendTask, serverReceiveTask);

                    using (var returnStream = serverReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "CrcConnection #1");
                    }
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var serverSendTask = crcServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var clientReceiveTask = crcClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(serverSendTask, clientReceiveTask);

                    using (var returnStream = clientReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "CrcConnection #2");
                    }
                }
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_CompressConnection()
        {
            TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
            listener.Start();
            var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

            TcpClient client = new TcpClient();
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

            var server = listener.EndAcceptSocket(listenerAcceptSocket);
            listener.Stop();

            using (var compressClient = new CompressConnection(new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            using (var compressServer = new CompressConnection(new BaseConnection(new SocketCap(server), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), Test_Library_Net_Connection.MaxReceiveCount, _bufferManager))
            {
                var clientConnectTask = compressClient.ConnectAsync(new TimeSpan(0, 0, 20));
                var serverConnectTask = compressServer.ConnectAsync(new TimeSpan(0, 0, 20));

                Task.WaitAll(clientConnectTask, serverConnectTask);

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var clientSendTask = compressClient.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var serverReceiveTask = compressServer.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(clientConnectTask, serverReceiveTask);

                    using (var returnStream = serverReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "CompressConnection #1");
                    }
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    var buffer = new byte[1024 * 8];
                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Seek(0, SeekOrigin.Begin);

                    var serverSendTask = compressServer.SendAsync(stream, new TimeSpan(0, 0, 20));
                    var clientReceiveTask = compressClient.ReceiveAsync(new TimeSpan(0, 0, 20));

                    Task.WaitAll(serverSendTask, clientReceiveTask);

                    using (var returnStream = clientReceiveTask.Result)
                    {
                        var buff2 = new byte[(int)returnStream.Length];
                        returnStream.Read(buff2, 0, buff2.Length);

                        Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "CompressConnection #2");
                    }
                }
            }

            client.Close();
            server.Close();
        }

        [Test]
        public void Test_SecureConnection()
        {
            for (int i = 0; i < 8; i++)
            {
                TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));
                listener.Start();
                var listenerAcceptSocket = listener.BeginAcceptSocket(null, null);

                TcpClient client = new TcpClient();
                client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 60000));

                var server = listener.EndAcceptSocket(listenerAcceptSocket);
                listener.Stop();

                DigitalSignature clientDigitalSignature = null;
                DigitalSignature serverDigitalSignature = null;

                if (_random.Next(0, 100) < 50)
                {
                    clientDigitalSignature = new DigitalSignature("NickName1", DigitalSignatureAlgorithm.Rsa2048_Sha256);
                }

                if (_random.Next(0, 100) < 50)
                {
                    serverDigitalSignature = new DigitalSignature("NickName2", DigitalSignatureAlgorithm.Rsa2048_Sha256);
                }

                SecureConnectionVersion clientVersion;
                SecureConnectionVersion serverVersion;

                {
                    clientVersion = SecureConnectionVersion.Version3;
                    serverVersion = SecureConnectionVersion.Version3;
                }

                {
                    //SecureConnectionVersion clientVersion = 0;
                    //SecureConnectionVersion serverVersion = 0;

                    //for (; ; )
                    //{
                    //    switch (_random.Next(0, 3))
                    //    {
                    //        case 0:
                    //            clientVersion = SecureConnectionVersion.Version2;
                    //            break;
                    //        case 1:
                    //            clientVersion = SecureConnectionVersion.Version3;
                    //            break;
                    //        case 2:
                    //            clientVersion = SecureConnectionVersion.Version2 | SecureConnectionVersion.Version3;
                    //            break;
                    //    }

                    //    switch (_random.Next(0, 3))
                    //    {
                    //        case 0:
                    //            serverVersion = SecureConnectionVersion.Version2;
                    //            break;
                    //        case 1:
                    //            serverVersion = SecureConnectionVersion.Version3;
                    //            break;
                    //        case 2:
                    //            serverVersion = SecureConnectionVersion.Version2 | SecureConnectionVersion.Version3;
                    //            break;
                    //    }

                    //    if ((clientVersion & serverVersion) != 0) break;
                    //}
                }

                //var TcpClient = new BaseConnection(client.Client, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager);
                using (var secureClient = new SecureConnection(clientVersion, SecureConnectionType.Connect, new BaseConnection(new SocketCap(client.Client), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), clientDigitalSignature, _bufferManager))
                using (var secureServer = new SecureConnection(serverVersion, SecureConnectionType.Accept, new BaseConnection(new SocketCap(server), null, Test_Library_Net_Connection.MaxReceiveCount, _bufferManager), serverDigitalSignature, _bufferManager))
                {
                    try
                    {
                        var clientConnectTask = secureClient.ConnectAsync(new TimeSpan(0, 0, 30));
                        var serverConnectTask = secureServer.ConnectAsync(new TimeSpan(0, 0, 30));

                        Task.WaitAll(clientConnectTask, serverConnectTask);

                        if (clientDigitalSignature != null)
                        {
                            if (secureServer.Certificate.ToString() != clientDigitalSignature.ToString()) throw new Exception();
                        }

                        if (serverDigitalSignature != null)
                        {
                            if (secureClient.Certificate.ToString() != serverDigitalSignature.ToString()) throw new Exception();
                        }

                        using (MemoryStream stream = new MemoryStream())
                        {
                            var buffer = new byte[1024 * 8];
                            _random.NextBytes(buffer);

                            stream.Write(buffer, 0, buffer.Length);
                            stream.Seek(0, SeekOrigin.Begin);

                            var clientSendTask = secureClient.SendAsync(stream, new TimeSpan(0, 0, 30));
                            var serverReceiveTask = secureServer.ReceiveAsync(new TimeSpan(0, 0, 30));

                            Task.WaitAll(clientConnectTask, serverReceiveTask);

                            using (var returnStream = serverReceiveTask.Result)
                            {
                                var buff2 = new byte[(int)returnStream.Length];
                                returnStream.Read(buff2, 0, buff2.Length);

                                Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "SecureConnection #1");
                            }
                        }

                        using (MemoryStream stream = new MemoryStream())
                        {
                            var buffer = new byte[1024 * 8];
                            _random.NextBytes(buffer);

                            stream.Write(buffer, 0, buffer.Length);
                            stream.Seek(0, SeekOrigin.Begin);

                            var serverSendTask = secureServer.SendAsync(stream, new TimeSpan(0, 0, 30));
                            var clientReceiveTask = secureClient.ReceiveAsync(new TimeSpan(0, 0, 30));

                            Task.WaitAll(serverSendTask, clientReceiveTask);

                            using (var returnStream = clientReceiveTask.Result)
                            {
                                var buff2 = new byte[(int)returnStream.Length];
                                returnStream.Read(buff2, 0, buff2.Length);

                                Assert.IsTrue(CollectionUtilities.Equals(buffer, buff2), "SecureConnection #2");
                            }
                        }
                    }
                    catch (AggregateException e)
                    {
                        Assert.IsTrue(e.InnerException.GetType() == typeof(ConnectionException)
                            && (clientVersion & serverVersion) == 0, "SecureConnection #Version test");
                    }
                }

                client.Close();
                server.Close();
            }
        }
    }
}
