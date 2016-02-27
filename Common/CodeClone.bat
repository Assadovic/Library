set BATDIR=%~dp0
cd %BATDIR%\..\

set TOOL="Library.Tools\bin\Debug\Library.Tools.exe"
IF EXIST %TOOL% call %TOOL% "CodeClone" "Common\CodeClone_Amoeba-Outopos_TargetList.txt" "Common\CodeClone_Amoeba-Outopos_WordList.txt"
