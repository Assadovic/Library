set BATDIR=%~dp0
cd %BATDIR%

call "Library.Tools\bin\Debug\Library.Tools.exe" "DigitalSignature_Create" %1 %2
