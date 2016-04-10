using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Library.Net.I2p
{
    static class I2pConverter
    {
        public static class Base32
        {
            static readonly char[] lowerTable = "abcdefghijklmnopqrstuvwxyz234567".ToCharArray();

            const int inBitsPerByte = 8;
            const int outBitsPerByte = 5;
            const int outBitMask = 0x1f;

            public static int CalculateLength(int length)
            {
                int lengthOut = ((length * inBitsPerByte) + (outBitsPerByte - 1)) / outBitsPerByte;
                return lengthOut;
            }

            public static int ToCharArray(byte[] inArray, int offsetIn, int lengthIn, char[] outArray, int offsetOut)
            {
                if (inArray == null) throw new ArgumentNullException(nameof(inArray));
                if (offsetIn < 0 || inArray.Length < offsetIn) throw new ArgumentOutOfRangeException(nameof(offsetIn));
                if (lengthIn < 0 || inArray.Length < lengthIn) throw new ArgumentOutOfRangeException(nameof(lengthIn));
                if (inArray.Length - offsetIn < lengthIn) throw new ArgumentOutOfRangeException();
                if (outArray == null) throw new ArgumentNullException(nameof(outArray));
                if (offsetOut < 0 || outArray.Length < offsetOut) throw new ArgumentOutOfRangeException(nameof(offsetOut));

                int lengthOut = Base32.CalculateLength(lengthIn);

                if (lengthOut < 0 || outArray.Length < lengthOut) throw new ArgumentOutOfRangeException(nameof(offsetOut));
                if (outArray.Length - offsetOut < lengthOut) throw new ArgumentOutOfRangeException();

                int positionIn = offsetIn;
                int positionOut = offsetOut;

                int queue = 0;
                int bitsInQueue = 0;

                for (int i = 0; i != lengthIn; ++i)
                {
                    queue <<= inBitsPerByte;
                    queue |= inArray[positionIn];
                    ++positionIn;
                    bitsInQueue += inBitsPerByte;
                    for (; bitsInQueue >= outBitsPerByte; bitsInQueue -= outBitsPerByte)
                    {
                        int outIndex = (queue >> (bitsInQueue - outBitsPerByte)) & outBitMask;
                        outArray[positionOut] = lowerTable[outIndex];
                        ++positionOut;
                    }
                }

                if (bitsInQueue != 0)
                {
                    int outIndex = (queue << (outBitsPerByte - bitsInQueue)) & outBitMask;
                    outArray[positionOut] = lowerTable[outIndex];
                    ++positionOut;
                    bitsInQueue = 0;
                }

                return lengthOut;
            }

            public static string ToString(byte[] inArray)
            {
                return ToString(inArray, 0, inArray.Length);
            }

            public static string ToString(byte[] inArray, int offset, int length)
            {
                char[] outArray = new char[CalculateLength(length)];
                ToCharArray(inArray, offset, length, outArray, 0);
                return new string(outArray);
            }
        }

        public static class Base64
        {
            public static byte[] FromString(string s)
            {
                return Convert.FromBase64String(s.Replace('-', '+').Replace('~', '/'));
            }

            public static byte[] FromCharArray(char[] inArray, int offset, int length)
            {
                return Base64.FromString(new string(inArray, offset, length));
            }
        }

        public static class Base32Address
        {
            public static string FromDestination(byte[] destination)
            {
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashResult = sha256.ComputeHash(destination);
                    return Base32.ToString(hashResult) + ".b32.i2p";
                }
            }

            public static string FromDestinationBase64(string destinationBase64)
            {
                byte[] destination = Base64.FromString(destinationBase64);
                return FromDestination(destination);
            }
        }
    }
}
