#include "stdafx.h"
#include "Xorshift.h"

#include "osrng.h"

Xorshift::Xorshift()
{
    p = 0;

    CryptoPP::AutoSeededRandomPool rng;
    rng.GenerateBlock((byte*)s, sizeof(uint64_t) * 64);
}

Xorshift::~Xorshift()
{

}

uint64_t Xorshift::next()
{
    uint64_t s0 = s[p];
    uint64_t s1 = s[p = (p + 1) & 63];

    s1 ^= s1 << 25; // a
    s1 ^= s1 >> 3;  // b
    s0 ^= s0 >> 49; // c
    
    return (s[p] = s0 ^ s1) * 8372773778140471301LL;
}
