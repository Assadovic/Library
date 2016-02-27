#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`
cd "${cwd}/../"

LD_LIBRARY_PATH="Library.Tools/bin/Debug"
TOOL="Library.Tools/bin/Debug/Library.Tools.exe"

mono ${TOOL} "Template" "Common/Template/Settings.txt"

cp "Common/C++/Library/build/Library.so" "Library/Assemblies/Library_x64.so"
cp "Common/C++/Library_Correction/build/Library_Correction.so" "Library.Correction/Assemblies/Library_Correction_x64.so"
cp "Common/C++/Library_Security/build/Library_Security.so" "Library.Security/Assemblies/Library_Security_x64.so"

