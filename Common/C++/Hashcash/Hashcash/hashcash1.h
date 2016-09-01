#pragma once

using namespace std;
using std::unique_ptr;

unique_ptr<byte> hashcash1_Create(byte* value, int32_t limit, int32_t timeout);
int32_t hashcash1_Verify(byte* key, byte* value);
