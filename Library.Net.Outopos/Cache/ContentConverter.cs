using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Library.Compression;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    static class ContentConverter
    {
        private enum ConvertCompressionAlgorithm : byte
        {
            None = 0,
            Deflate = 1,
        }

        private enum ConvertCryptoAlgorithm : byte
        {
            Aes256 = 0,
        }

        private enum ConvertHashAlgorithm : byte
        {
            Sha256 = 0,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static RandomNumberGenerator _random = RandomNumberGenerator.Create();

        private static Stream Compress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var targetStream = new RangeStream(stream, true);

            List<KeyValuePair<byte, Stream>> list = new List<KeyValuePair<byte, Stream>>();

            try
            {
                targetStream.Seek(0, SeekOrigin.Begin);

                BufferStream deflateBufferStream = null;

                try
                {
                    deflateBufferStream = new BufferStream(_bufferManager);

                    using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                    {
                        byte[] compressBuffer = null;

                        try
                        {
                            compressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                            int i = -1;

                            while ((i = targetStream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                            {
                                deflateStream.Write(compressBuffer, 0, i);
                            }
                        }
                        finally
                        {
                            if (compressBuffer != null)
                            {
                                _bufferManager.ReturnBuffer(compressBuffer);
                            }
                        }
                    }

                    deflateBufferStream.Seek(0, SeekOrigin.Begin);

                    list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.Deflate, deflateBufferStream));
                }
                catch (Exception)
                {
                    if (deflateBufferStream != null)
                    {
                        deflateBufferStream.Dispose();
                    }
                }
            }
            catch (Exception)
            {

            }

            list.Add(new KeyValuePair<byte, Stream>((byte)ConvertCompressionAlgorithm.None, targetStream));

            list.Sort((x, y) =>
            {
                int c = x.Value.Length.CompareTo(y.Value.Length);
                if (c != 0) return c;

                return x.Key.CompareTo(y.Key);
            });

#if DEBUG
            if (list[0].Value.Length != targetStream.Length)
            {
                Debug.WriteLine("ContentConverter Compress {3} : {0}→{1} {2}",
                    NetworkConverter.ToSizeString(targetStream.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length),
                    NetworkConverter.ToSizeString(list[0].Value.Length - targetStream.Length),
                    (ConvertCompressionAlgorithm)list[0].Key);
            }
#endif

            for (int i = 1; i < list.Count; i++)
            {
                list[i].Value.Dispose();
            }

            BufferStream metadataStream = new BufferStream(_bufferManager);
            metadataStream.WriteByte((byte)list[0].Key);

            return new UniteStream(metadataStream, list[0].Value);
        }

        private static Stream Decompress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                var targetStream = new RangeStream(stream, true);

                byte type = (byte)targetStream.ReadByte();

                if (type == (byte)ConvertCompressionAlgorithm.None)
                {
                    return new RangeStream(targetStream);
                }
                else if (type == (byte)ConvertCompressionAlgorithm.Deflate)
                {
                    using (Stream dataStream = new WrapperStream(targetStream, true))
                    {
                        BufferStream deflateBufferStream = null;

                        try
                        {
                            deflateBufferStream = new BufferStream(_bufferManager);

                            using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                            {
                                byte[] decompressBuffer = null;

                                try
                                {
                                    decompressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                    int i = -1;

                                    while ((i = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                    {
                                        deflateBufferStream.Write(decompressBuffer, 0, i);
                                    }
                                }
                                finally
                                {
                                    if (decompressBuffer != null)
                                    {
                                        _bufferManager.ReturnBuffer(decompressBuffer);
                                    }
                                }
                            }

                            deflateBufferStream.Seek(0, SeekOrigin.Begin);

#if DEBUG
                            Debug.WriteLine("ContentConverter Decompress {3} : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length),
                                ConvertCompressionAlgorithm.Deflate);
#endif

                            return deflateBufferStream;
                        }
                        catch (Exception)
                        {
                            if (deflateBufferStream != null)
                            {
                                deflateBufferStream.Dispose();
                            }
                        }
                    }
                }

                throw new ArgumentException("ArgumentException");
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream Encrypt(Stream stream, IExchangeEncrypt publicKey)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));

            try
            {
                BufferStream outStream = null;

                try
                {
                    outStream = new BufferStream(_bufferManager);
                    outStream.WriteByte((byte)ConvertCryptoAlgorithm.Aes256);

                    byte[] cryptoKey = new byte[32];
                    _random.GetBytes(cryptoKey);

                    {
                        var encryptedBuffer = Exchange.Encrypt(publicKey, cryptoKey);
                        outStream.Write(NetworkConverter.GetBytes((int)encryptedBuffer.Length), 0, 4);
                        outStream.Write(encryptedBuffer, 0, encryptedBuffer.Length);
                    }

                    byte[] iv = new byte[32];
                    _random.GetBytes(iv);
                    outStream.Write(iv, 0, iv.Length);

                    using (Stream inStream = new WrapperStream(stream, true))
                    {
                        using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                        using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateEncryptor(cryptoKey, iv), CryptoStreamMode.Read))
                        {
                            byte[] buffer = null;

                            try
                            {
                                buffer = _bufferManager.TakeBuffer(1024 * 4);

                                int i = -1;

                                while ((i = cs.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outStream.Write(buffer, 0, i);
                                }
                            }
                            finally
                            {
                                if (buffer != null)
                                {
                                    _bufferManager.ReturnBuffer(buffer);
                                }
                            }
                        }
                    }

                    outStream.Seek(0, SeekOrigin.Begin);
                }
                catch (Exception)
                {
                    if (outStream != null)
                    {
                        outStream.Dispose();
                    }

                    throw;
                }

                return outStream;
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream Decrypt(Stream stream, IExchangeDecrypt privateKey)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));

            try
            {
                byte type = (byte)stream.ReadByte();

                if (type == (byte)ConvertCryptoAlgorithm.Aes256)
                {
                    byte[] cryptoKey;

                    {
                        byte[] lengthBuffer = new byte[4];
                        if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) throw new ArgumentException();
                        int length = NetworkConverter.ToInt32(lengthBuffer);

                        byte[] encryptedBuffer = new byte[length];
                        if (stream.Read(encryptedBuffer, 0, encryptedBuffer.Length) != encryptedBuffer.Length) throw new ArgumentException();

                        cryptoKey = Exchange.Decrypt(privateKey, encryptedBuffer);
                    }

                    BufferStream outStream = null;

                    try
                    {
                        outStream = new BufferStream(_bufferManager);

                        using (Stream dataStream = new WrapperStream(stream, true))
                        {
                            var iv = new byte[32];
                            dataStream.Read(iv, 0, iv.Length);

                            using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                            using (var inStream = new RangeStream(dataStream, dataStream.Position, dataStream.Length - dataStream.Position))
                            using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey, iv), CryptoStreamMode.Read))
                            {
                                byte[] buffer = null;

                                try
                                {
                                    buffer = _bufferManager.TakeBuffer(1024 * 4);

                                    int i = -1;

                                    while ((i = cs.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        outStream.Write(buffer, 0, i);
                                    }
                                }
                                finally
                                {
                                    _bufferManager.ReturnBuffer(buffer);
                                }
                            }
                        }

                        outStream.Seek(0, SeekOrigin.Begin);
                    }
                    catch (Exception)
                    {
                        if (outStream != null)
                        {
                            outStream.Dispose();
                        }

                        throw;
                    }

                    return outStream;
                }

                throw new NotSupportedException();
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream AddPadding(Stream stream, int size)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                byte[] seedBuffer = new byte[4];
                _random.GetBytes(seedBuffer);
                Random random = new Random(NetworkConverter.ToInt32(seedBuffer));

                BufferStream metadataStream = new BufferStream(_bufferManager);
                byte[] lengthBuffer = NetworkConverter.GetBytes((int)stream.Length);
                metadataStream.Write(lengthBuffer, 0, lengthBuffer.Length);

                int paddingLength = size - ((int)stream.Length + 4);

                BufferStream paddingStream = new BufferStream(_bufferManager);

                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = _bufferManager.TakeBuffer(1024);

                        while (paddingLength > 0)
                        {
                            int writeSize = Math.Min(paddingLength, buffer.Length);

                            random.NextBytes(buffer);
                            paddingStream.Write(buffer, 0, writeSize);

                            paddingLength -= writeSize;
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(buffer);
                    }
                }

                return new UniteStream(metadataStream, new WrapperStream(stream, true), paddingStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemovePadding(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                byte[] lengthBuffer = new byte[4];
                stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                int length = NetworkConverter.ToInt32(lengthBuffer);

                return new RangeStream(stream, 4, length, true);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream AddType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Type
            if (type != null)
            {
                BufferStream bufferStream = new BufferStream(_bufferManager);
                bufferStream.SetLength(4);
                bufferStream.Seek(4, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(type);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 4), 0, 4);

                streams.Add(bufferStream);
            }

            streams.Add(new WrapperStream(stream, true));

            return new UniteStream(streams);
        }

        private static Stream RemoveType(Stream stream, string type)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            Encoding encoding = new UTF8Encoding(false);

            byte[] lengthBuffer = new byte[4];
            if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) throw new FormatException();
            int length = NetworkConverter.ToInt32(lengthBuffer);

            using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
            {
                using (StreamReader reader = new StreamReader(rangeStream, encoding))
                {
                    if (type != reader.ReadToEnd()) throw new FormatException();
                }
            }

            return new RangeStream(stream, true);
        }

        private static Stream AddHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                var targetStream = new RangeStream(stream, true);

                BufferStream metadataStream = new BufferStream(_bufferManager);
                metadataStream.WriteByte((byte)ConvertHashAlgorithm.Sha256);

                targetStream.Seek(0, SeekOrigin.Begin);
                var hash = Sha256.ComputeHash(targetStream);

                BufferStream hashStream = new BufferStream(_bufferManager);
                hashStream.Write(hash, 0, hash.Length);

                return new UniteStream(metadataStream, targetStream, hashStream);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemoveHash(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            byte type = (byte)stream.ReadByte();

            if (type == (byte)ConvertHashAlgorithm.Sha256)
            {
                Stream dataStream = null;

                try
                {
                    byte[] hash = new byte[32];

                    using (RangeStream hashStream = new RangeStream(stream, stream.Length - 32, 32, true))
                    {
                        hashStream.Read(hash, 0, hash.Length);
                    }

                    dataStream = new RangeStream(stream, 1, stream.Length - (1 + 32));
                    if (!Unsafe.Equals(hash, Sha256.ComputeHash(dataStream))) throw new FormatException();

                    dataStream.Seek(0, SeekOrigin.Begin);

                    return dataStream;
                }
                catch (Exception)
                {
                    if (dataStream != null)
                    {
                        dataStream.Dispose();
                    }

                    throw;
                }
            }

            throw new NotSupportedException();
        }

        public static ArraySegment<byte> ToBroadcastMessageBlock(BroadcastMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            ArraySegment<byte> value;

            using (Stream messageStream = message.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(messageStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "BroadcastMessage"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static BroadcastMessage FromBroadcastMessageBlock(ArraySegment<byte> message)
        {
            if (message.Array == null) throw new ArgumentException("message.Array", nameof(message));

            try
            {
                using (Stream typeStream = new MemoryStream(message.Array, message.Offset, message.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "BroadcastMessage"))
                using (Stream messageStream = ContentConverter.Decompress(compressStream))
                {
                    return BroadcastMessage.Import(messageStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToMulticastMessageBlock(MulticastMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            ArraySegment<byte> value;

            using (Stream messageStream = message.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(messageStream))
            using (Stream typeStream = ContentConverter.AddType(compressStream, "MulticastMessage"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static MulticastMessage FromMulticastMessageBlock(ArraySegment<byte> message)
        {
            if (message.Array == null) throw new ArgumentException("message.Array", nameof(message));

            try
            {
                using (Stream typeStream = new MemoryStream(message.Array, message.Offset, message.Count))
                using (Stream compressStream = ContentConverter.RemoveType(typeStream, "MulticastMessage"))
                using (Stream messageStream = ContentConverter.Decompress(compressStream))
                {
                    return MulticastMessage.Import(messageStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static ArraySegment<byte> ToUnicastMessageBlock(UnicastMessage message, IExchangeEncrypt publicKey)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));

            ArraySegment<byte> value;

            using (Stream messageStream = message.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(messageStream))
            using (Stream paddingStream = ContentConverter.AddPadding(compressStream, 1024 * 256))
            using (Stream hashStream = ContentConverter.AddHash(paddingStream))
            using (Stream cryptostream = ContentConverter.Encrypt(hashStream, publicKey))
            using (Stream typeStream = ContentConverter.AddType(cryptostream, "UnicastMessage"))
            {
                value = new ArraySegment<byte>(_bufferManager.TakeBuffer((int)typeStream.Length), 0, (int)typeStream.Length);
                typeStream.Read(value.Array, value.Offset, value.Count);
            }

            return value;
        }

        public static UnicastMessage FromUnicastMessageBlock(ArraySegment<byte> message, IExchangeDecrypt privateKey)
        {
            if (message.Array == null) throw new ArgumentException("message.Array", nameof(message));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));

            try
            {
                using (Stream typeStream = new MemoryStream(message.Array, message.Offset, message.Count))
                using (Stream cryptoStream = ContentConverter.RemoveType(typeStream, "UnicastMessage"))
                using (Stream hashStream = ContentConverter.Decrypt(cryptoStream, privateKey))
                using (Stream paddingStream = ContentConverter.RemoveHash(hashStream))
                using (Stream compressStream = ContentConverter.RemovePadding(paddingStream))
                using (Stream messageStream = ContentConverter.Decompress(compressStream))
                {
                    return UnicastMessage.Import(messageStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
