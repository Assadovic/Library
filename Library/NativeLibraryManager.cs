﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Library
{
    public class NativeLibraryManager : ManagerBase
    {
        private volatile bool _disposed;

        IntPtr _moduleHandle = IntPtr.Zero;

#if Windows
        static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool FreeLibrary(IntPtr hModule);
        }
#endif

#if Linux
        static class NativeMethods
        {
            const int RTLD_NOW = 2;

            [DllImport("libdl.so")]
            private static extern IntPtr dlopen(String fileName, int flags);

            [DllImport("libdl.so")]
            private static extern IntPtr dlsym(IntPtr handle, String symbol);

            [DllImport("libdl.so")]
            private static extern int dlclose(IntPtr handle);

            [DllImport("libdl.so")]
            private static extern IntPtr dlerror();

            public static IntPtr LoadLibrary(string fileName)
            {
                return dlopen(fileName, RTLD_NOW);
            }

            public static void FreeLibrary(IntPtr handle)
            {
                dlclose(handle);
            }

            public static IntPtr GetProcAddress(IntPtr dllHandle, string name)
            {
                // clear previous errors if any
                dlerror();
                var res = dlsym(dllHandle, name);
                var errPtr = dlerror();
                if (errPtr != IntPtr.Zero)
                {
                    throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
                }
                return res;
            }
        }
#endif

        public NativeLibraryManager(string path)
        {
            _moduleHandle = NativeMethods.LoadLibrary(path);
        }

        public T GetMethod<T>(string method)
            where T : class
        {
            if (!typeof(T).IsSubclassOf(typeof(Delegate)))
            {
                throw new InvalidOperationException(typeof(T).Name + " is not a delegate type");
            }

            IntPtr methodHandle = NativeMethods.GetProcAddress(_moduleHandle, method);
            return Marshal.GetDelegateForFunctionPointer(methodHandle, typeof(T)) as T;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }

            if (_moduleHandle != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.FreeLibrary(_moduleHandle);
                }
                catch (Exception)
                {

                }

                _moduleHandle = IntPtr.Zero;
            }
        }
    }
}