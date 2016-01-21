using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Library.Collections;
using Library.Io;

namespace Library.Security
{
    public class Miner
    {
        private readonly CashAlgorithm _cashAlgorithm;
        private readonly int _limit;
        private readonly TimeSpan _computationTime;

        private volatile bool _isCanceled;

        public Miner(CashAlgorithm cashAlgorithm, int limit, TimeSpan computationTime)
        {
            _cashAlgorithm = cashAlgorithm;
            _limit = limit;
            _computationTime = computationTime;
        }

        public CashAlgorithm CashAlgorithm
        {
            get
            {
                return _cashAlgorithm;
            }
        }

        public int Limit
        {
            get
            {
                return _limit;
            }
        }

        public TimeSpan ComputationTime
        {
            get
            {
                return _computationTime;
            }
        }

        public void Cancel()
        {
            _isCanceled = true;
        }

        public Cash Create(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            if (this.Limit == 0 || this.ComputationTime <= TimeSpan.Zero) return null;

            if (this.CashAlgorithm == CashAlgorithm.Version1)
            {
                _isCanceled = false;

                var minerUtilities = new MinerUtilities();

                try
                {
                    var task = Task.Factory.StartNew(() =>
                    {
                        var key = minerUtilities.Create_1(Sha256.ComputeHash(stream), this.Limit, this.ComputationTime);
                        return new Cash(CashAlgorithm.Version1, key);
                    });

                    while (!task.IsCompleted)
                    {
                        if (_isCanceled) minerUtilities.Cancel();

                        Thread.Sleep(1000);
                    }

                    return task.Result;
                }
                catch (AggregateException e)
                {
                    throw e.InnerExceptions.FirstOrDefault();
                }
            }

            return null;
        }

        public static int Verify(Cash cash, Stream stream)
        {
            if (stream == null) throw new ArgumentNullException("stream");

            if (cash == null) return 0;

            if (cash.CashAlgorithm == CashAlgorithm.Version1)
            {
                var minerUtilities = new MinerUtilities();

                return minerUtilities.Verify_1(cash.Key, Sha256.ComputeHash(stream));
            }

            return 0;
        }

        public static int Sample(TimeSpan computationTime)
        {
            var miner = new Miner(CashAlgorithm.Version1, -1, computationTime);

            var buffer = new byte[32];
            {
                var random = RandomNumberGenerator.Create();
                random.GetBytes(buffer);
            }

            using (var stream = new MemoryStream(buffer))
            {
                var cash = miner.Create(new WrapperStream(stream, true));

                stream.Seek(0, SeekOrigin.Begin);
                return Miner.Verify(cash, new WrapperStream(stream, true));
            }
        }

        private class MinerUtilities
        {
            private static string _path;

            static MinerUtilities()
            {
                OperatingSystem osInfo = Environment.OSVersion;

                if (osInfo.Platform == PlatformID.Win32NT)
                {
                    if (System.Environment.Is64BitProcess)
                    {
                        _path = "Assemblies/Hashcash_x64.exe";
                    }
                    else
                    {
                        _path = "Assemblies/Hashcash_x86.exe";
                    }
                }
            }

            private LockedList<Process> _processes = new LockedList<Process>();

            public byte[] Create_1(byte[] value, int limit, TimeSpan computationTime)
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value.Length != 32) throw new ArgumentOutOfRangeException("value");

#if DEBUG
                var info = new ProcessStartInfo(@"C:\Local\Projects\Alliance-Network\Library\Library.Tools\bin\Debug\Library.Tools.exe");
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
#else
                var info = new ProcessStartInfo(_path);
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
#endif

                {
                    if (limit < 0) limit = -1;

                    int timeout;

                    if (computationTime < TimeSpan.Zero) timeout = -1;
                    else timeout = (int)computationTime.TotalSeconds;

#if DEBUG
                    info.Arguments = string.Format(
                        "Watcher \"{0}\" \"{1}\" hashcash1 create {2} {3} {4}",
                        Process.GetCurrentProcess().Id,
                        _path,
                        NetworkConverter.ToHexString(value),
                        limit,
                        timeout);
#else
                    info.Arguments = string.Format(
                        "hashcash1 create {0} {1} {2}",
                        NetworkConverter.ToHexString(value),
                        limit,
                        timeout);
#endif
                }

                using (var process = Process.Start(info))
                {
                    _processes.Add(process);

                    try
                    {
                        process.PriorityClass = ProcessPriorityClass.Idle;

                        try
                        {
                            var result = process.StandardOutput.ReadLine();

                            process.WaitForExit();
                            if (process.ExitCode != 0) throw new MinerException();

                            return NetworkConverter.FromHexString(result);
                        }
                        catch (MinerException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            throw new MinerException(e.Message, e);
                        }
                    }
                    finally
                    {
                        _processes.Remove(process);
                    }
                }
            }

            public int Verify_1(byte[] key, byte[] value)
            {
                if (key == null) throw new ArgumentNullException("key");
                if (key.Length != 32) throw new ArgumentOutOfRangeException("key");
                if (value == null) throw new ArgumentNullException("value");
                if (value.Length != 32) throw new ArgumentOutOfRangeException("value");

                var bufferManager = BufferManager.Instance;

                try
                {

                    byte[] result;

                    {
                        byte[] buffer = bufferManager.TakeBuffer(64);
                        Unsafe.Copy(key, 0, buffer, 0, 32);
                        Unsafe.Copy(value, 0, buffer, 32, 32);

                        result = Sha256.ComputeHash(buffer, 0, 64);

                        bufferManager.ReturnBuffer(buffer);
                    }

                    int count = 0;

                    for (int i = 0; i < 32; i++)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            if (((result[i] << j) & 0x80) == 0) count++;
                            else goto End;
                        }
                    }
                End:

                    return count;
                }
                catch (Exception)
                {
                    return 0;
                }
            }

            public void Cancel()
            {
#if DEBUG
                string taskkill = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");

                foreach (var process in _processes.ToArray())
                {
                    try
                    {
                        using (var killer = new System.Diagnostics.Process())
                        {
                            killer.StartInfo.FileName = taskkill;
                            killer.StartInfo.Arguments = string.Format("/PID {0} /T /F", process.Id);
                            killer.StartInfo.CreateNoWindow = true;
                            killer.StartInfo.UseShellExecute = false;
                            killer.Start();
                            killer.WaitForExit();
                        }

                        process.Dispose();
                    }
                    catch (Exception)
                    {

                    }
                }
#else
                foreach (var process in _processes.ToArray())
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {

                    }
                }
#endif

                _processes.Clear();
            }
        }
    }

    [Serializable]
    class MinerException : Exception
    {
        public MinerException() : base() { }
        public MinerException(string message) : base(message) { }
        public MinerException(string message, Exception innerException) : base(message, innerException) { }
    }
}
