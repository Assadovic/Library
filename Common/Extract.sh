#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`
cd "${cwd}/../"

cp "Common/C++/Library/build/x86/Library.so" "Library/Assemblies/Library_x86.so"
cp "Common/C++/Library/build/x64/Library.so" "Library/Assemblies/Library_x64.so"
cp "Common/C++/Library_Correction/build/x86/Library_Correction.so" "Library.Correction/Assemblies/Library_Correction_x86.so"
cp "Common/C++/Library_Correction/build/x64/Library_Correction.so" "Library.Correction/Assemblies/Library_Correction_x64.so"
cp "Common/C++/Library_Security/build/x86/Library_Security.so" "Library.Security/Assemblies/Library_Security_x86.so"
cp "Common/C++/Library_Security/build/x64/Library_Security.so" "Library.Security/Assemblies/Library_Security_x64.so"

