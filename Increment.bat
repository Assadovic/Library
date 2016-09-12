set BATDIR=%~dp0
cd %BATDIR%

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF NOT EXIST %TOOL% exit

call %TOOL% "Increment" Library\Library.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Collections\Library.Collections.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Compression\Library.Compression.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Configuration\Library.Configuration.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Io\Library.Io.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net\Library.Net.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Amoeba\Library.Net.Amoeba.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Connections\Library.Net.Connections.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.I2p\Library.Net.I2p.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Proxy\Library.Net.Proxy.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Net.Upnp\Library.Net.Upnp.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Security\Library.Security.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Tools\Library.Tools.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.UnitTest\Library.UnitTest.csproj Properties\AssemblyInfo.cs
call %TOOL% "Increment" Library.Update\Library.Update.csproj Properties\AssemblyInfo.cs
