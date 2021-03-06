﻿using System;
using System.Diagnostics;
using System.IO;
using Library.Compression;
using Library.Io;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Compression")]
    public class Test_Library_Compression
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_Xz()
        {
            using (MemoryStream stream1 = new MemoryStream())
            using (FileStream stream2 = new FileStream("temp.xz", FileMode.Create))
            //using (MemoryStream stream2 = new MemoryStream())
            using (MemoryStream stream3 = new MemoryStream())
            {
                for (int i = 0; i < 4; i++)
                {
                    byte[] buffer = new byte[1024 * 1024];
                    _random.NextBytes(buffer);
                    stream1.Write(buffer, 0, buffer.Length);
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();

                stream1.Seek(0, SeekOrigin.Begin);
                Xz.Compress(new WrapperStream(stream1, true), new WrapperStream(stream2, true), _bufferManager);

                stream2.Seek(0, SeekOrigin.Begin);
                Xz.Decompress(new WrapperStream(stream2, true), new WrapperStream(stream3, true), _bufferManager);

                sw.Stop();
                Console.WriteLine(string.Format("Xz: {0}", sw.Elapsed.ToString()));

                stream1.Seek(0, SeekOrigin.Begin);
                stream3.Seek(0, SeekOrigin.Begin);

                Assert.AreEqual(stream1.Length, stream3.Length);

                for (;;)
                {
                    byte[] buffer1 = new byte[1024 * 32];
                    int buffer1Length;
                    byte[] buffer2 = new byte[1024 * 32];
                    int buffer2Length;

                    if ((buffer1Length = stream1.Read(buffer1, 0, buffer1.Length)) <= 0) break;
                    if ((buffer2Length = stream3.Read(buffer2, 0, buffer2.Length)) <= 0) break;

                    Assert.IsTrue(CollectionUtils.Equals(buffer1, 0, buffer2, 0, buffer1Length));
                }
            }
        }

        [Test]
        public void Test_Lzma()
        {
            using (MemoryStream stream1 = new MemoryStream())
            using (FileStream stream2 = new FileStream("temp.lzma", FileMode.Create))
            //using (MemoryStream stream2 = new MemoryStream())
            using (MemoryStream stream3 = new MemoryStream())
            {
                for (int i = 0; i < 4; i++)
                {
                    byte[] buffer = new byte[1024 * 1024];
                    _random.NextBytes(buffer);
                    stream1.Write(buffer, 0, buffer.Length);
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();

                stream1.Seek(0, SeekOrigin.Begin);
                Lzma.Compress(new WrapperStream(stream1, true), new WrapperStream(stream2, true), _bufferManager);
                stream2.Seek(0, SeekOrigin.Begin);
                Lzma.Decompress(new WrapperStream(stream2, true), new WrapperStream(stream3, true), _bufferManager);

                sw.Stop();
                Console.WriteLine(string.Format("Lzma: {0}", sw.Elapsed.ToString()));

                stream1.Seek(0, SeekOrigin.Begin);
                stream3.Seek(0, SeekOrigin.Begin);

                Assert.AreEqual(stream1.Length, stream3.Length);

                for (;;)
                {
                    byte[] buffer1 = new byte[1024 * 32];
                    int buffer1Length;
                    byte[] buffer2 = new byte[1024 * 32];
                    int buffer2Length;

                    if ((buffer1Length = stream1.Read(buffer1, 0, buffer1.Length)) <= 0) break;
                    if ((buffer2Length = stream3.Read(buffer2, 0, buffer2.Length)) <= 0) break;

                    Assert.IsTrue(CollectionUtils.Equals(buffer1, 0, buffer2, 0, buffer1Length));
                }
            }
        }
    }
}
