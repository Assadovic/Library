set BATDIR=%~dp0
cd %BATDIR%

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF EXIST %TOOL% call %TOOL% "increment" %1 %2
