using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Library.Security
{
    public class Hashcash1
    {
        private static string _path;

        private volatile Process _runningProcess;

        private readonly object _lockObject = new object();

        static Hashcash1()
        {
            OperatingSystem osInfo = Environment.OSVersion;

            if (osInfo.Platform == PlatformID.Win32NT)
            {
                if (System.Environment.Is64BitProcess)
                {
                    _path = "Assembly/Hashcash_x64.exe";
                }
                else
                {
                    _path = "Assembly/Hashcash_x86.exe";
                }
            }
        }

        public byte[] Create(byte[] value, TimeSpan timeout)
        {
            lock (_lockObject)
            {
                try
                {
                    var info = new ProcessStartInfo(_path);
                    info.CreateNoWindow = true;
                    info.UseShellExecute = false;
                    info.RedirectStandardOutput = true;

                    info.Arguments = string.Format("hashcash1 create {0} {1}", NetworkConverter.ToHexString(value), (int)timeout.TotalSeconds);

                    _runningProcess = Process.Start(info);
                    _runningProcess.PriorityClass = ProcessPriorityClass.Idle;

                    try
                    {
                        var result = _runningProcess.StandardOutput.ReadLine();

                        _runningProcess.WaitForExit();
                        if (_runningProcess.ExitCode != 0) return null;

                        return NetworkConverter.FromHexString(result);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
                finally
                {
                    _runningProcess = null;
                }
            }
        }

        public int Verify(byte[] key, byte[] value)
        {
            var info = new ProcessStartInfo(_path);
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;

            info.Arguments = string.Format("hashcash1 verify {0} {1}", NetworkConverter.ToHexString(key), NetworkConverter.ToHexString(value));

            var process = Process.Start(info);

            try
            {
                var result = process.StandardOutput.ReadLine();

                process.WaitForExit();
                if (process.ExitCode != 0) return -1;

                return int.Parse(result);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public void Cancel()
        {
            try
            {
                _runningProcess.Kill();
            }
            catch (Exception)
            {

            }
        }
    }
}
