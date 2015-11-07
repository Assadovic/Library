#pragma once

class Xorshift
{
private:
    uint64_t s[64];
    int p;

public:
    Xorshift();
    ~Xorshift();

    uint64_t next();
};
