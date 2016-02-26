set BATDIR=%~dp0
cd %BATDIR%\..\

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF NOT EXIST %TOOL% exit

call %TOOL% "Define" on "Windows" "Common\Define.txt"
call %TOOL% "Define" off "Linux" "Common\Define.txt"
