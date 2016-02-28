#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`

cd ${cwd}/Library/Library
make 

cd ${cwd}/Library_Correction/Library_Correction
make

cd ${cwd}/Library_Security/Library_Security
make
