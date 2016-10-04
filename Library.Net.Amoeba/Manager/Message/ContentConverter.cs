using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    static class ContentConverter
    {
        private enum ConvertCompressionAlgorithm
        {
            None = 0,
            Deflate = 1,
        }

        private enum ConvertCryptoAlgorithm : byte
        {
            Aes256 = 0,
        }

        private static BufferManager _bufferManager = BufferManager.Instance;
        private static RandomNumberGenerator _random = RandomNumberGenerator.Create();

        private static Stream AddVersion(Stream stream, int version)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var streams = new List<Stream>();

            {
                var bufferStream = new BufferStream(_bufferManager);
                VintUtils.WriteVint(bufferStream, version);

                streams.Add(bufferStream);
            }

            streams.Add(new WrapperStream(stream, true));

            return new UniteStream(streams);
        }

        private static Stream RemoveVersion(Stream stream, int version)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (VintUtils.GetVint(stream) != version) throw new FormatException();

            return new RangeStream(stream, true);
        }

        private static Stream Compress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var targetStream = new RangeStream(stream, true);

            var list = new List<KeyValuePair<int, Stream>>();

            try
            {
                targetStream.Seek(0, SeekOrigin.Begin);

                BufferStream deflateBufferStream = null;

                try
                {
                    deflateBufferStream = new BufferStream(_bufferManager);

                    using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                    using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                    {
                        int length;

                        while ((length = targetStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                        {
                            deflateStream.Write(safeBuffer.Value, 0, length);
                        }
                    }

                    deflateBufferStream.Seek(0, SeekOrigin.Begin);

                    list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.Deflate, deflateBufferStream));
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

            list.Add(new KeyValuePair<int, Stream>((int)ConvertCompressionAlgorithm.None, targetStream));

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

            var metadataStream = new BufferStream(_bufferManager);
            VintUtils.WriteVint(metadataStream, list[0].Key);

            return new UniteStream(metadataStream, list[0].Value);
        }

        private static Stream Decompress(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                var targetStream = new RangeStream(stream, true);

                var type = (int)VintUtils.GetVint(targetStream);

                if (type == (int)ConvertCompressionAlgorithm.None)
                {
                    return new RangeStream(targetStream);
                }
                else if (type == (int)ConvertCompressionAlgorithm.Deflate)
                {
                    using (Stream dataStream = new WrapperStream(targetStream, true))
                    {
                        BufferStream deflateBufferStream = null;

                        try
                        {
                            deflateBufferStream = new BufferStream(_bufferManager);

                            using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                            using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                            {
                                int length;

                                while ((length = deflateStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                                {
                                    deflateBufferStream.Write(safeBuffer.Value, 0, length);

                                    if (deflateBufferStream.Length > 1024 * 1024 * 256) throw new Exception("too large");
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

                            throw;
                        }
                    }
                }
                else
                {
                    throw new ArgumentException("ArgumentException");
                }
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
                    VintUtils.WriteVint(outStream, (int)ConvertCryptoAlgorithm.Aes256);

                    byte[] cryptoKey = new byte[32];
                    _random.GetBytes(cryptoKey);

                    {
                        var encryptedBuffer = Exchange.Encrypt(publicKey, cryptoKey);
                        VintUtils.WriteVint(outStream, (int)encryptedBuffer.Length);
                        outStream.Write(encryptedBuffer, 0, encryptedBuffer.Length);
                    }

                    byte[] iv = new byte[32];
                    _random.GetBytes(iv);
                    outStream.Write(iv, 0, iv.Length);

                    using (Stream inStream = new WrapperStream(stream, true))
                    using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                    using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateEncryptor(cryptoKey, iv), CryptoStreamMode.Read))
                    using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                    {
                        int length;

                        while ((length = cs.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                        {
                            outStream.Write(safeBuffer.Value, 0, length);
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
                var type = (int)VintUtils.GetVint(stream);

                if (type == (int)ConvertCryptoAlgorithm.Aes256)
                {
                    byte[] cryptoKey;

                    {
                        int length = (int)VintUtils.GetVint(stream);

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

                            using (var inStream = new RangeStream(dataStream, dataStream.Position, dataStream.Length - dataStream.Position))
                            using (var rijndael = new RijndaelManaged() { KeySize = 256, BlockSize = 256, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
                            using (CryptoStream cs = new CryptoStream(inStream, rijndael.CreateDecryptor(cryptoKey, iv), CryptoStreamMode.Read))
                            using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                            {
                                int length;

                                while ((length = cs.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                                {
                                    outStream.Write(safeBuffer.Value, 0, length);
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

            var streams = new List<Stream>();

            try
            {
                var tempStream = new BufferStream(_bufferManager);
                streams.Add(tempStream);
                {
                    using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                    {
                        int length;

                        while ((length = stream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                        {
                            tempStream.Write(safeBuffer.Value, 0, length);
                        }
                    }
                }

                for (; size < 1024 * 1024 * 32; size *= 2)
                {
                    if (size > tempStream.Length) break;
                }

                byte[] seedBuffer = new byte[4];
                _random.GetBytes(seedBuffer);
                var random = new Random(NetworkConverter.ToInt32(seedBuffer));

                var metadataStream = new BufferStream(_bufferManager);
                streams.Add(metadataStream);
                VintUtils.WriteVint(metadataStream, tempStream.Length);

                int paddingLength = size - (int)(metadataStream.Length + tempStream.Length);

                var paddingStream = new BufferStream(_bufferManager);
                streams.Add(paddingStream);
                {
                    using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                    {
                        while (paddingLength > 0)
                        {
                            int writeSize = Math.Min(paddingLength, safeBuffer.Value.Length);

                            random.NextBytes(safeBuffer.Value);
                            paddingStream.Write(safeBuffer.Value, 0, writeSize);

                            paddingLength -= writeSize;
                        }
                    }
                }

                return new UniteStream(streams);
            }
            catch (Exception e)
            {
                foreach (var targetStream in streams)
                {
                    targetStream.Dispose();
                }

                throw new ArgumentException(e.Message, e);
            }
        }

        private static Stream RemovePadding(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                int length = (int)VintUtils.GetVint(stream);
                return new RangeStream(stream, stream.Position, length, true);
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        public static Stream ToStream<T>(T message)
            where T : ItemBase<T>
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var bufferStream = new BufferStream(_bufferManager);

            using (Stream messageStream = message.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(messageStream))
            using (Stream versionStream = ContentConverter.AddVersion(compressStream, 0))
            {
                using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                {
                    int length;

                    while ((length = versionStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                    {
                        bufferStream.Write(safeBuffer.Value, 0, length);
                    }
                }
            }

            return bufferStream;
        }

        public static T FromStream<T>(Stream stream)
            where T : ItemBase<T>
        {
            if (stream == null) throw new ArgumentException("stream", nameof(stream));

            try
            {
                using (Stream versionStream = new WrapperStream(stream, true))
                using (Stream compressStream = ContentConverter.RemoveVersion(versionStream, 0))
                using (Stream messageStream = ContentConverter.Decompress(compressStream))
                {
                    return ItemBase<T>.Import(messageStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Stream ToCryptoStream<T>(T message, IExchangeEncrypt publicKey)
            where T : ItemBase<T>
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));

            var bufferStream = new BufferStream(_bufferManager);

            using (Stream messageStream = message.Export(_bufferManager))
            using (Stream compressStream = ContentConverter.Compress(messageStream))
            using (Stream paddingStream = ContentConverter.AddPadding(compressStream, 1024 * 256))
            using (Stream cryptostream = ContentConverter.Encrypt(paddingStream, publicKey))
            using (Stream versionStream = ContentConverter.AddVersion(cryptostream, 0))
            {
                using (var safeBuffer = _bufferManager.CreateSafeBuffer(1024 * 4))
                {
                    int length;

                    while ((length = versionStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                    {
                        bufferStream.Write(safeBuffer.Value, 0, length);
                    }
                }
            }

            return bufferStream;
        }

        public static T FromCryptoStream<T>(Stream stream, IExchangeDecrypt privateKey)
            where T : ItemBase<T>
        {
            if (stream == null) throw new ArgumentException("stream", nameof(stream));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));

            try
            {
                using (Stream versionStream = new WrapperStream(stream, true))
                using (Stream cryptoStream = ContentConverter.RemoveVersion(versionStream, 0))
                using (Stream paddingStream = ContentConverter.Decrypt(cryptoStream, privateKey))
                using (Stream compressStream = ContentConverter.RemovePadding(paddingStream))
                using (Stream messageStream = ContentConverter.Decompress(compressStream))
                {
                    return ItemBase<T>.Import(messageStream, _bufferManager);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
