set BATDIR=%~dp0
cd %BATDIR%\..\

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF NOT EXIST %TOOL% exit

call %TOOL% "Define" on "Linux" "Common\Define.txt"
call %TOOL% "Define" off "Windows" "Common\Define.txt"