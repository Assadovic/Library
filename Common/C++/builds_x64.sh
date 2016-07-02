#!/bin/sh
cwd=`dirname "${0}"`
expr "${0}" : "/.*" > /dev/null || cwd=`(cd "${cwd}" && pwd)`

export CFLAGS="-shared -m64 -fPIC -Wall -O3 -pipe -D UNIX"
export CXXFLAGS="-shared -m64 -fPIC -Wall -O3 -pipe -D UNIX" 
export BUILD="../build/x64"

cd ${cwd}/Library/Library
make 

cd ${cwd}/Library_Correction/Library_Correction
make

cd ${cwd}/Library_Security/Library_Security
make
