﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;

namespace Library
{
    public interface IDeepCloneable<T>
    {
        T DeepClone();
    }
}
