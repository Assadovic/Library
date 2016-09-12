using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Library.Io
{
    public class ItemStreamWriter : ManagerBase
    {
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));
        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[8]);

        private BufferManager _bufferManager;

        private List<Stream> _streamList;
        private Stream _stream;

        private bool _disposed;

        public ItemStreamWriter(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;

            _streamList = new List<Stream>();
            _stream = new BufferStream(_bufferManager);
            _streamList.Add(_stream);
        }

        public void Add(int id, Stream targetStream)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, targetStream.Length);

            _streamList.Add(targetStream);

            _stream = new BufferStream(_bufferManager);
            _streamList.Add(_stream);
        }

        public void Write(int id, string value)
        {
            Encoding encoding = _threadLocalEncoding.Value;

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(encoding.GetMaxByteCount(value.Length)))
            {
                var length = encoding.GetBytes(value, 0, value.Length, safeBuffer.Value, 0);

                VintUtils.WriteVint(_stream, id);
                VintUtils.WriteVint(_stream, length);
                _stream.Write(safeBuffer.Value, 0, length);
            }
        }

        public void Write(int id, byte[] value)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, value.Length);
            _stream.Write(value, 0, value.Length);
        }

        public void Write(int id, byte[] value, int offset, int length)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, length);
            _stream.Write(value, offset, length);
        }

        public void Write(int id, Enum value)
        {
            this.Write(id, value.ToString());
        }

        public void Write(int id, DateTime value)
        {
            this.Write(id, value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
        }

        public void Write(int id, byte value)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, 1);
            _stream.WriteByte(value);
        }

        public void Write(int id, short value)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, 2);
            _stream.Write(NetworkConverter.GetBytes(value), 0, 2);
        }

        public void Write(int id, int value)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, 4);
            _stream.Write(NetworkConverter.GetBytes(value), 0, 4);
        }

        public void Write(int id, long value)
        {
            VintUtils.WriteVint(_stream, id);
            VintUtils.WriteVint(_stream, 8);
            _stream.Write(NetworkConverter.GetBytes(value), 0, 8);
        }

        public Stream GetStream()
        {
            return new UniteStream(_streamList);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }
        }
    }
}
