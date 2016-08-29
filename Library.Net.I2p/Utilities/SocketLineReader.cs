using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Library;

namespace Library.Net.I2p
{
    class SocketLineReader : ManagerBase
    {
        private Socket _socket;
        private Encoding _encoding;

        public SocketLineReader(Socket socket, Encoding encoding)
        {
            _socket = socket;
            _encoding = encoding;
        }

        public string ReadLine()
        {
            using (var stream = new MemoryStream())
            {
                for (;;)
                {
                    var buffer = new byte[1];
                    _socket.Receive(buffer);
                    stream.Write(buffer, 0, 1);

                    if (buffer[0] == '\n') break;
                }

                stream.Seek(0, SeekOrigin.Begin);

                using (var reader = new StreamReader(stream, _encoding))
                {
                    return reader.ReadToEnd().TrimEnd('\r', '\n');
                }
            }
        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}
