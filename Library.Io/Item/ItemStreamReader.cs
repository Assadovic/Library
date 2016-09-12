using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Library.Io
{
    public class ItemStreamReader : ManagerBase
    {
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));
        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[8]);

        private Stream _stream;
        private BufferManager _bufferManager;

        private bool _disposed;

        public ItemStreamReader(Stream stream, BufferManager bufferManager)
        {
            _stream = stream;
            _bufferManager = bufferManager;
        }

        public int GetId()
        {
            return (int)VintUtils.GetVint(_stream);
        }

        public Stream GetStream()
        {
            long length = VintUtils.GetVint(_stream);
            if (length < 0) throw new ArgumentException();

            return new RangeStream(_stream, _stream.Position, length, true);
        }

        public byte[] GetBytes()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length < 0) throw new ArgumentException();

            byte[] buffer = new byte[length];
            _stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public string GetString()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length < 0) throw new ArgumentException();

            Encoding encoding = _threadLocalEncoding.Value;

            using (var safeBuffer = _bufferManager.CreateSafeBuffer(length))
            {
                _stream.Read(safeBuffer.Value, 0, length);

                return encoding.GetString(safeBuffer.Value, 0, length);
            }
        }

        public T GetEnum<T>()
            where T : struct
        {
            return EnumUtils<T>.Parse(this.GetString());
        }

        public DateTime GetDateTime()
        {
            return DateTime.ParseExact(this.GetString(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
        }

        public byte GetByte()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length != 1) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;
            if (_stream.Read(buffer, 0, 1) != 1) throw new ArgumentException();

            return buffer[0];
        }

        public short GetShort()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length != 2) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;
            if (_stream.Read(buffer, 0, 2) != 2) throw new ArgumentException();

            return NetworkConverter.ToInt16(buffer);
        }

        public int GetInt()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length != 4) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;
            if (_stream.Read(buffer, 0, 4) != 4) throw new ArgumentException();

            return NetworkConverter.ToInt32(buffer);
        }

        public long GetLong()
        {
            var length = (int)VintUtils.GetVint(_stream);
            if (length != 8) throw new ArgumentException();

            byte[] buffer = _threadLocalBuffer.Value;
            if (_stream.Read(buffer, 0, 8) != 8) throw new ArgumentException();

            return NetworkConverter.ToInt64(buffer);
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
