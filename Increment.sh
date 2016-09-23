#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`
cd "${cwd}"

LD_LIBRARY_PATH="Library.Tools/bin/Debug"
TOOL="Library.Tools/bin/Debug/Library.Tools.exe"

mono ${TOOL} "Increment" Library/Library.csproj Library/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Collections/Library.Collections.csproj Library.Collections/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Compression/Library.Compression.csproj Library.Compression/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Configuration/Library.Configuration.csproj Library.Configuration/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Io/Library.Io.csproj Library.Io/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Messaging/Library.Messaging.csproj Library.Messaging/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net/Library.Net.csproj Library.Net/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net.Amoeba/Library.Net.Amoeba.csproj Library.Net.Amoeba/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net.Connections/Library.Net.Connections.csproj Library.Net.Connections/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net.I2p/Library.Net.I2p.csproj Library.Net.I2p/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net.Proxy/Library.Net.Proxy.csproj Library.Net.Proxy/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Net.Upnp/Library.Net.Upnp.csproj Library.Net.Upnp/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Security/Library.Security.csproj Library.Security/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Tools/Library.Tools.csproj Library.Tools/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.UnitTest/Library.UnitTest.csproj Library.UnitTest/Properties/AssemblyInfo.cs
mono ${TOOL} "Increment" Library.Update/Library.Update.csproj Library.Update/Properties/AssemblyInfo.cs
