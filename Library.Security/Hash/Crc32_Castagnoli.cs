﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Library.Security
{
    public unsafe static class Crc32_Castagnoli
    {
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));

        private static NativeLibraryManager _nativeLibraryManager;

        delegate uint ComputeDelegate(uint x, byte* src, int len);
        private static ComputeDelegate _compute;

        static Crc32_Castagnoli()
        {
            try
            {
#if Windows
                if (System.Environment.Is64BitProcess)
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Security_x64.dll");
                }
                else
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Security_x86.dll");
                }
#endif

#if Unix
                if (System.Environment.Is64BitProcess)
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Security_x64.so");
                }
                else
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assemblies/Library_Security_x86.so");
                }
#endif

                _compute = _nativeLibraryManager.GetMethod<ComputeDelegate>("compute_Crc32_Castagnoli");
            }
            catch (Exception e)
            {
                Log.Warning(e);
            }
        }

        public static byte[] ComputeHash(byte[] buffer, int offset, int length)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || (buffer.Length - offset) < length) throw new ArgumentOutOfRangeException(nameof(length));

            uint x = 0xFFFFFFFF;

            fixed (byte* p_buffer = buffer)
            {
                var t_buffer = p_buffer + offset;

                x = _compute(x, t_buffer, length);
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }

        /// <summary>
        /// ハッシュを生成する
        /// </summary>
        /// <param name="buffer">ハッシュ値を計算するbyte配列</param>
        public static byte[] ComputeHash(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            return Crc32_Castagnoli.ComputeHash(buffer, 0, buffer.Length);
        }

        public static byte[] ComputeHash(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            return Crc32_Castagnoli.ComputeHash(_threadLocalEncoding.Value.GetBytes(value));
        }

        public static byte[] ComputeHash(ArraySegment<byte> value)
        {
            if (value.Array == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Crc32_Castagnoli.ComputeHash(value.Array, value.Offset, value.Count);
        }

        public static byte[] ComputeHash(Stream inputStream)
        {
            if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));

            uint x = 0xFFFFFFFF;

            byte[] buffer = new byte[1024 * 4];
            int length = 0;

            fixed (byte* p_buffer = buffer)
            {
                while ((length = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    x = _compute(x, p_buffer, length);
                }
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }

        public static byte[] ComputeHash(IList<ArraySegment<byte>> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            uint x = 0xFFFFFFFF;

            for (int i = 0; i < value.Count && value[i].Array != null; i++)
            {
                fixed (byte* p_buffer = value[i].Array)
                {
                    var t_buffer = p_buffer + value[i].Offset;

                    x = _compute(x, t_buffer, value[i].Count);
                }
            }

            return NetworkConverter.GetBytes(x ^ 0xFFFFFFFF);
        }
    }
}
