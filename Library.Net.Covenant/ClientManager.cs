using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Library.Io;
using Library.Net.Connections;
using Library.Net.Proxy;

namespace Library.Net.Covenant
{
    public delegate Cap CreateCapEventHandler(object sender, string uri);

    class ClientManager : ManagerBase, IThisLock
    {
        private BufferManager _bufferManager;
        private BandwidthLimit _bandwidthLimit;

        private CreateCapEventHandler _createCapEvent;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private const int _maxReceiveCount = 1024 * 1024 * 8;

        public ClientManager(BufferManager bufferManager, BandwidthLimit bandwidthLimit)
        {
            _bufferManager = bufferManager;
            _bandwidthLimit = bandwidthLimit;
        }

        public BandwidthLimit BandwidthLimit
        {
            get
            {
                return _bandwidthLimit;
            }
        }

        public CreateCapEventHandler CreateCapEvent
        {
            set
            {
                lock (this.ThisLock)
                {
                    _createCapEvent = value;
                }
            }
        }

        protected virtual Cap OnCreateCapEvent(string uri)
        {
            return _createCapEvent?.Invoke(this, uri);
        }

        public Connection CreateConnection(string uri, out ProtocolVersion version, ProtocolType type)
        {
            version = 0;

            var garbages = new List<IDisposable>();

            try
            {
                Connection connection = null;
                {
                    var cap = this.OnCreateCapEvent(uri);
                    if (cap == null) goto End;

                    garbages.Add(cap);

                    connection = new BaseConnection(cap, _bandwidthLimit, _maxReceiveCount, _bufferManager);
                    garbages.Add(connection);

                    End:;
                }

                if (connection == null) return null;

                version = this.Handshake(connection, type);

                if (version == ProtocolVersion.Version1)
                {
                    if (type == ProtocolType.Search)
                    {
                        var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
                        garbages.Add(compressConnection);

                        compressConnection.Connect(new TimeSpan(0, 0, 10));

                        return compressConnection;
                    }
                    else if (type == ProtocolType.Exchange)
                    {
                        var compressConnection = new CompressConnection(connection, _maxReceiveCount, _bufferManager);
                        garbages.Add(compressConnection);

                        compressConnection.Connect(new TimeSpan(0, 0, 10));

                        return compressConnection;
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            catch (Exception)
            {
                foreach (var item in garbages)
                {
                    item.Dispose();
                }
            }

            return null;
        }

        private ProtocolVersion Handshake(Connection connection, ProtocolType protocolType)
        {
            var timeout = new TimeSpan(0, 0, 30);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            ProtocolVersion protocolVersion;

            {
                var myProtocolVersion = ProtocolVersion.Version1;

                using (BufferStream stream = new BufferStream(_bufferManager))
                using (XmlTextWriter xml = new XmlTextWriter(stream, new UTF8Encoding(false)))
                {
                    xml.WriteStartDocument();

                    xml.WriteStartElement("Protocol");

                    if (myProtocolVersion.HasFlag(ProtocolVersion.Version1))
                    {
                        xml.WriteStartElement("Covenant");
                        xml.WriteAttributeString("Version", "1");
                        xml.WriteEndElement(); //Covenant
                    }

                    xml.WriteEndElement(); //Protocol

                    xml.WriteEndDocument();
                    xml.Flush();
                    stream.Flush();

                    stream.Seek(0, SeekOrigin.Begin);
                    connection.Send(stream, timeout - stopwatch.Elapsed);
                }

                var otherProtocolVersion = (ProtocolVersion)0;

                using (Stream stream = connection.Receive(timeout - stopwatch.Elapsed))
                using (XmlTextReader xml = new XmlTextReader(stream))
                {
                    while (xml.Read())
                    {
                        if (xml.NodeType == XmlNodeType.Element)
                        {
                            if (xml.LocalName == "Covenant")
                            {
                                var version = xml.GetAttribute("Version");

                                if (version == "1")
                                {
                                    otherProtocolVersion |= ProtocolVersion.Version1;
                                }
                            }
                        }
                    }
                }

                protocolVersion = myProtocolVersion & otherProtocolVersion;
            }

            if (protocolVersion.HasFlag(ProtocolVersion.Version1))
            {
                using (BufferStream stream = new BufferStream(_bufferManager))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    if (protocolType.HasFlag(ProtocolType.Search))
                    {
                        writer.Write("Search");
                    }
                    else if (protocolType.HasFlag(ProtocolType.Exchange))
                    {
                        writer.Write("Exchange");
                    }

                    writer.Flush();
                    stream.Flush();

                    stream.Seek(0, SeekOrigin.Begin);
                    connection.Send(stream, timeout - stopwatch.Elapsed);
                }
            }

            return protocolVersion;
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
    class ClientManagerException : ManagerException
    {
        public ClientManagerException() : base() { }
        public ClientManagerException(string message) : base(message) { }
        public ClientManagerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
