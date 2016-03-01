#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`
cd "${cwd}"

LD_LIBRARY_PATH="Library.UnitTest/bin/Debug"
nunit-console "Library.UnitTest/bin/Debug/Library.UnitTest.dll"

