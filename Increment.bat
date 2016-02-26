set BATDIR=%~dp0
cd %BATDIR%

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF NOT EXIST %TOOL% exit

call %TOOL% "Increment" Library\Library.csproj Library\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Collections\Library.Collections.csproj Library.Collections\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Compression\Library.Compression.csproj Library.Compression\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Configuration\Library.Configuration.csproj Library.Configuration\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Io\Library.Io.csproj Library.Io\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net\Library.Net.csproj Library.Net\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Amoeba\Library.Net.Amoeba.csproj Library.Net.Amoeba\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Connections\Library.Net.Connections.csproj Library.Net.Connections\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.I2p\Library.Net.I2p.csproj Library.Net.I2p\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Outopos\Library.Net.Outopos.csproj Library.Net.Outopos\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Proxy\Library.Net.Proxy.csproj Library.Net.Proxy\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Upnp\Library.Net.Upnp.csproj Library.Net.Upnp\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Security\Library.Security.csproj Library.Security\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Tools\Library.Tools.csproj Library.Tools\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.UnitTest\Library.UnitTest.csproj Library.UnitTest\Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Update\Library.Update.csproj Library.Update\Properties\AssemblyInfo.cs
