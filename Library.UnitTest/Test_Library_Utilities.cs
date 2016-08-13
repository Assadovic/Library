using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Library.Utilities;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Utilities")]
    public class Test_Library_Utilities
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_VIntUtils()
        {
            using (var stream = new MemoryStream())
            {
                for (int i = 0; i < 1024 * 1024; i++)
                {
                    var v = _random.Next();
                    v >>= _random.Next(0, 32);

                    VintUtils.WriteVint1(stream, v);
                    stream.Seek(0, SeekOrigin.Begin);

                    Assert.AreEqual(v, VintUtils.GetVint1(stream), "VintUtilities #Int");

                    stream.Seek(0, SeekOrigin.Begin);
                }
            }

            using (var stream = new MemoryStream())
            {
                for (int i = 0; i < 1024 * 1024; i++)
                {
                    var v = (long)_random.Next() << 32 | (uint)_random.Next();
                    v >>= _random.Next(0, 64);

                    VintUtils.WriteVint4(stream, v);
                    stream.Seek(0, SeekOrigin.Begin);

                    Assert.AreEqual(v, VintUtils.GetVint4(stream), "VintUtilities #Long");

                    stream.Seek(0, SeekOrigin.Begin);
                }
            }
        }
    }
}
