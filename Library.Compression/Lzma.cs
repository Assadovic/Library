using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Library.Io;

namespace Library.Compression
{
    public static class Lzma
    {
        private static string _path;

        static Lzma()
        {
#if Windows
            if (System.Environment.Is64BitProcess)
            {
                _path = "Assemblies/Xz_x64.exe";
            }
            else
            {
                _path = "Assemblies/Xz_x86.exe";
            }
#endif

#if Linux
            _path = "xz";
#endif
        }

        public static void Compress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            var info = new ProcessStartInfo(_path);
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            info.Arguments = "--compress --format=lzma -4 --threads=1 --stdout";

            using (var inCacheStream = new CacheStream(inStream, 1024 * 32, bufferManager))
            using (var outCacheStream = new CacheStream(outStream, 1024 * 32, bufferManager))
            {
                using (Process process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        Log.Error(e.Data);
                    };

                    Exception threadException = null;

                    var thread = new Thread(() =>
                    {
                        try
                        {
                            using (var standardOutputStream = process.StandardOutput.BaseStream)
                            using (var safeBuffer = bufferManager.CreateSafeBuffer(1024 * 32))
                            {
                                int length;

                                while ((length = standardOutputStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                                {
                                    outCacheStream.Write(safeBuffer.Value, 0, length);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            threadException = e;
                        }
                    });
                    thread.IsBackground = true;
                    thread.Start();

                    try
                    {
                        using (var standardInputStream = process.StandardInput.BaseStream)
                        using (var safeBuffer = bufferManager.CreateSafeBuffer(1024 * 32))
                        {
                            int length;

                            while ((length = inCacheStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                            {
                                standardInputStream.Write(safeBuffer.Value, 0, length);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }

                    thread.Join();
                    if (threadException != null) throw threadException;

                    process.WaitForExit();
                }
            }
        }

        public static void Decompress(Stream inStream, Stream outStream, BufferManager bufferManager)
        {
            var info = new ProcessStartInfo(_path);
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            info.Arguments = "--decompress --format=lzma --memlimit-decompress=256MiB --stdout";

            using (var inCacheStream = new CacheStream(inStream, 1024 * 32, bufferManager))
            using (var outCacheStream = new CacheStream(outStream, 1024 * 32, bufferManager))
            {
                using (Process process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        Log.Error(e.Data);
                    };

                    Exception threadException = null;

                    var thread = new Thread(() =>
                    {
                        try
                        {
                            using (var standardOutputStream = process.StandardOutput.BaseStream)
                            using (var safeBuffer = bufferManager.CreateSafeBuffer(1024 * 32))
                            {
                                int length;

                                while ((length = standardOutputStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                                {
                                    outCacheStream.Write(safeBuffer.Value, 0, length);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            threadException = e;
                        }
                    });
                    thread.IsBackground = true;
                    thread.Start();

                    try
                    {
                        using (var standardInputStream = process.StandardInput.BaseStream)
                        using (var safeBuffer = bufferManager.CreateSafeBuffer(1024 * 32))
                        {
                            int length;

                            while ((length = inCacheStream.Read(safeBuffer.Value, 0, safeBuffer.Value.Length)) > 0)
                            {
                                standardInputStream.Write(safeBuffer.Value, 0, length);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }

                    thread.Join();
                    if (threadException != null) throw threadException;

                    process.WaitForExit();
                }
            }
        }
    }
}
