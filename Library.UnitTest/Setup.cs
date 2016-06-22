using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Library.UnitTest
{
    [SetUpFixture]
    public class Setup
    {
        [SetUp]
        public void Init()
        {
#if Linux
            Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
#endif
        }
    }
}
