#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`
cd "${cwd}/../"

LD_LIBRARY_PATH="Library.Tools/bin/Debug"
TOOL="Library.Tools/bin/Debug/Library.Tools.exe"

mono ${TOOL} "CodeClone" "Common/CodeClone_Amoeba-Outopos_TargetList.txt" "Common/CodeClone_Amoeba-Outopos_WordList.txt"
