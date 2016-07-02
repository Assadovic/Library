// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

#include <stdio.h>
#include <stdint.h>
#include <string>
#include <iostream>
#include <time.h>
#include <tchar.h>

#ifdef WINDOWS
#include "targetver.h"
#include <windows.h>
#endif

#ifdef UNIX
typedef unsigned char byte;
#endif

#ifndef PORTABLE_32_BIT_TEST
    #if _WIN64 || __amd64__
    #define PORTABLE_64_BIT
    #else
    #define PORTABLE_32_BIT
    #endif
#else
    #define PORTABLE_32_BIT
#endif
