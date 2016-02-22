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
        [OneTimeSetUp]
        public void Init()
        {
            Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
        }
    }
}
