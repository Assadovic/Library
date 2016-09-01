#include "stdafx.h"
#include "hashcash1.h"

using namespace std;
using std::unique_ptr;

inline char getHexValue(int32_t c)
{
    if (c < 10) return (char)(c + '0');
    else return (char)(c - 10 + 'a');
}

string toHexString(byte* value, size_t length)
{
    unique_ptr<char> chars(new char[(length * 2) + 1]);

    {
        byte* t_value = value;
        char* t_chars = chars.get();

        for (int32_t i = length - 1; i >= 0; i--)
        {
            byte b = *t_value++;

            *t_chars++ = getHexValue(b >> 4);
            *t_chars++ = getHexValue(b & 0x0F);
        }

        *t_chars = '\0';
    }

    string result = chars.get();

    chars.release();

    return result;
}

unique_ptr<byte> fromHexString(string value, size_t& size)
{
    if (value.length() % 2 != 0)
    {
        value = "0" + value;
    }

    size = (value.length() / 2) * sizeof(byte);
    unique_ptr<byte> buffer(new byte[size / sizeof(byte)]);

    {
        byte* t_buffer = buffer.get();
        char* t_value = (char*)value.c_str();

        for (int32_t i = size - 1; i >= 0; i--)
        {
            int32_t i1 = 0, i2 = 0;

            if ('0' <= *t_value && *t_value <= '9')
            {
                i1 = *t_value - '0';
            }
            else if ('a' <= *t_value && *t_value <= 'f')
            {
                i1 = (*t_value - 'a') + 10;
            }
            else if ('A' <= *t_value && *t_value <= 'F')
            {
                i1 = (*t_value - 'A') + 10;
            }

            t_value++;

            if ('0' <= *t_value && *t_value <= '9')
            {
                i2 = *t_value - '0';
            }
            else if ('a' <= *t_value && *t_value <= 'f')
            {
                i2 = (*t_value - 'a') + 10;
            }
            else if ('A' <= *t_value && *t_value <= 'F')
            {
                i2 = (*t_value - 'A') + 10;
            }

            t_value++;

            *t_buffer++ = (byte)((i1 << 4) | i2);
        }
    }

    return buffer;
}

//#define TEST

#ifdef TEST
int main2(int argc, char* argv[])
#else
int main(int argc, char* argv[])
#endif
{
    try
    {
        if ((string)argv[1] == "hashcash1")
        {
            if ((string)argv[2] == "create")
            {
                size_t valueSize;
                unique_ptr<byte> value = fromHexString((string)argv[3], valueSize);

                int32_t limit = atoi(argv[4]);
                int32_t timeout = atoi(argv[5]);

                unique_ptr<byte> key = hashcash1_Create(value.get(), limit, timeout);

                cout << toHexString(key.get(), 32) << endl;
            }
            else if ((string)argv[2] == "verify")
            {
                size_t keySize;
                unique_ptr<byte> key = fromHexString((string)argv[3], keySize);
                if (keySize != 32) return 1;

                size_t valueSize;
                unique_ptr<byte> value = fromHexString((string)argv[4], valueSize);
                if (valueSize != 32) return 1;

                int32_t count = hashcash1_Verify(key.get(), value.get());

                cout << count << endl;
            }
        }
    }
    catch (exception&)
    {
        return 1;
    }

    return 0;
}

#ifdef TEST
int main(int argc, char* argv[])
{
    clock_t clockStart, clockEnd;
    clockStart = clock();

    //{
    //    const int32_t count = 6;

    //    char* arguments[count];
    //    arguments[1] = "hashcash1";
    //    arguments[2] = "create";
    //    arguments[3] = "0101010101010101010101010101010101010101010101010101010101010101";
    //    arguments[4] = "256";
    //    arguments[5] = "1800";

    //    main2(count, arguments);
    //}

    {
        const uint32_t count = 5;

        char* arguments[count];
        arguments[1] = "hashcash1";
        arguments[2] = "verify";

        // 5seconds, 22bit
        //arguments[3] = "e8637a65315e17953424e0081ed288ed64895b5be8b29274caf95a7d5dcce9d6";
        // 60seconds, 26bit
        //arguments[3] = "dd9582a578328dafd1b65e0c5a375cfd5179c14c439c198aef5b08733354f26f";
        // 1800seconds, 34bit
        arguments[3] = "ce2025a78ee303fc1cada1e609ca144c15b0b9c25f24452126a467cae17bc920";

        arguments[4] = "0101010101010101010101010101010101010101010101010101010101010101";

        main2(count, arguments);
    }

    clockEnd = clock();

    cout << (clockEnd - clockStart) << endl;

    return 0;
}
#endif
