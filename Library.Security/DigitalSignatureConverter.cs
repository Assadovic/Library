﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Library.Io;
using Library.Net.Lair;
using Library.Security;
using System.Diagnostics;

namespace Library.Security
{
    public static class DigitalSignatureConverter
    {
        private enum CompressionAlgorithm
        {
            None = 0,
            Deflate = 1,
        }

        private static BufferManager _bufferManager = new BufferManager();
        private static Regex _base64Regex = new Regex(@"^([a-zA-Z0-9\-_]*).*?$", RegexOptions.Compiled | RegexOptions.Singleline);

        static DigitalSignatureConverter()
        {

        }

        private static Stream ToStream<T>(ItemBase<T> item)
                where T : ItemBase<T>
        {
            Stream stream = null;

            try
            {
                stream = item.Export(_bufferManager);
                List<KeyValuePair<int, Stream>> list = new List<KeyValuePair<int, Stream>>();

                try
                {
                    BufferStream deflateBufferStream = new BufferStream(_bufferManager);
                    byte[] compressBuffer = null;

                    try
                    {
                        compressBuffer = _bufferManager.TakeBuffer(1024 * 1024);

                        using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                        {
                            int i = -1;

                            while ((i = stream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                            {
                                deflateStream.Write(compressBuffer, 0, i);
                            }
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(compressBuffer);
                    }

                    deflateBufferStream.Seek(0, SeekOrigin.Begin);
                    list.Add(new KeyValuePair<int, Stream>(1, deflateBufferStream));
                }
                catch (Exception)
                {

                }

                list.Add(new KeyValuePair<int, Stream>(0, new RangeStream(stream, true)));

                list.Sort(new Comparison<KeyValuePair<int, Stream>>((KeyValuePair<int, Stream> x, KeyValuePair<int, Stream> y) =>
                {
                    return x.Value.Length.CompareTo(y.Value.Length);
                }));

#if DEBUG
                if (list[0].Value.Length != stream.Length)
                {
                    Debug.WriteLine("LairConverter ToStream : {0}→{1} {2}",
                        NetworkConverter.ToSizeString(stream.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length),
                        NetworkConverter.ToSizeString(list[0].Value.Length - stream.Length));
                }
#endif

                for (int i = 1; i < list.Count; i++)
                {
                    list[i].Value.Dispose();
                }

                BufferStream headerStream = new BufferStream(_bufferManager);
                headerStream.WriteByte((byte)list[0].Key);

                var dataStream = new AddStream(headerStream, list[0].Value);

                MemoryStream crcStream = new MemoryStream(Crc32_Castagnoli.ComputeHash(dataStream));
                return new AddStream(dataStream, crcStream);
            }
            catch (Exception ex)
            {
                if (stream != null)
                    stream.Dispose();

                throw new ArgumentException(ex.Message, ex);
            }
        }

        private static T FromStream<T>(Stream stream)
            where T : ItemBase<T>
        {
            try
            {
                using (Stream verifyStream = new RangeStream(stream, 0, stream.Length - 4, true))
                {
                    byte[] verifyCrc = Crc32_Castagnoli.ComputeHash(verifyStream);
                    byte[] orignalCrc = new byte[4];

                    using (RangeStream crcStream = new RangeStream(stream, stream.Length - 4, 4, true))
                    {
                        crcStream.Read(orignalCrc, 0, orignalCrc.Length);
                    }

                    if (!Collection.Equals(verifyCrc, orignalCrc))
                        throw new ArgumentException("Crc Error");
                }

                stream.Seek(0, SeekOrigin.Begin);
                byte version = (byte)stream.ReadByte();

                using (Stream dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position - 4, true))
                {
                    if (version == (byte)CompressionAlgorithm.None)
                    {
                        return ItemBase<T>.Import(dataStream, _bufferManager);
                    }
                    else if (version == (byte)CompressionAlgorithm.Deflate)
                    {
                        using (BufferStream deflateBufferStream = new BufferStream(_bufferManager))
                        {
                            byte[] decompressBuffer = null;

                            try
                            {
                                decompressBuffer = _bufferManager.TakeBuffer(1024 * 1024);

                                using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                                {
                                    int i = -1;

                                    while ((i = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                    {
                                        deflateBufferStream.Write(decompressBuffer, 0, i);
                                    }
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(decompressBuffer);
                            }

#if DEBUG
                            Debug.WriteLine("LairConverter FromStream : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(dataStream.Length),
                                NetworkConverter.ToSizeString(deflateBufferStream.Length),
                                NetworkConverter.ToSizeString(dataStream.Length - deflateBufferStream.Length));
#endif

                            deflateBufferStream.Seek(0, SeekOrigin.Begin);

                            return ItemBase<T>.Import(deflateBufferStream, _bufferManager);
                        }
                    }
                    else
                    {
                        throw new ArgumentException("ArgumentException");
                    }
                }
            }
            catch (Exception e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        private static string ToBase64String(Stream stream)
        {
            byte[] buffer = null;

            try
            {
                buffer = _bufferManager.TakeBuffer((int)stream.Length);
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)stream.Length);

                return NetworkConverter.ToBase64String(buffer, 0, (int)stream.Length).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
            finally
            {
                if (buffer != null)
                {
                    _bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        private static Stream FromBase64String(string value)
        {
            var match = _base64Regex.Match(value);
            if (!match.Success) throw new ArgumentException();

            value = match.Groups[1].Value;

            string patting = "";

            switch (value.Length % 4)
            {
                case 1:
                case 3:
                    patting = "=";
                    break;

                case 2:
                    patting = "==";
                    break;
            }

            return new MemoryStream(NetworkConverter.FromBase64String(value.Replace('-', '+').Replace('_', '/') + patting));
        }

        public static Stream ToSignatureStream(DigitalSignature item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                return DigitalSignatureConverter.ToStream<DigitalSignature>(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static DigitalSignature FromSignatureStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                return DigitalSignatureConverter.FromStream<DigitalSignature>(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Stream ToCertificateStream(Certificate item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                return DigitalSignatureConverter.ToStream<Certificate>(item);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Certificate FromCertificateStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            try
            {
                return DigitalSignatureConverter.FromStream<Certificate>(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
