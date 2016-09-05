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
                    var v = (long)_random.Next() << 32 | (uint)_random.Next();
                    v >>= _random.Next(0, 64);

                    VintUtils.WriteVint(stream, v);
                    stream.Seek(0, SeekOrigin.Begin);

                    Assert.AreEqual(v, VintUtils.GetVint(stream), "VintUtilities #Long");

                    stream.Seek(0, SeekOrigin.Begin);
                }
            }
        }
    }
}
