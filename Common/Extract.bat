set BATDIR=%~dp0
cd %BATDIR%\..\

copy /Y /B "Common\C++\Library\Release\Library.dll" "Library\Assemblies\Library_x86.dll"
copy /Y /B "Common\C++\Library\x64\Release\Library.dll" "Library\Assemblies\Library_x64.dll"

copy /Y /B "Common\C++\Library_Correction\Release\Library_Correction.dll" "Library.Correction\Assemblies\Library_Correction_x86.dll"
copy /Y /B "Common\C++\Library_Correction\x64\Release\Library_Correction.dll" "Library.Correction\Assemblies\Library_Correction_x64.dll"

copy /Y /B "Common\C++\Library_Security\Release\Library_Security.dll" "Library.Security\Assemblies\Library_Security_x86.dll"
copy /Y /B "Common\C++\Library_Security\x64\Release\Library_Security.dll" "Library.Security\Assemblies\Library_Security_x64.dll"

copy /Y /B "Common\C++\Hashcash\Release\Hashcash.exe" "Library.Security\Assemblies\Hashcash_x86.exe"
copy /Y /B "Common\C++\Hashcash\x64\Release\Hashcash.exe" "Library.Security\Assemblies\Hashcash_x64.exe"