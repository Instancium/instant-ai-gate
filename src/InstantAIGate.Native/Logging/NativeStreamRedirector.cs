namespace InstantAIGate.Native.Logging;

using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Intercepts OS-level stderr streams. Fully cross-platform compatible.
/// </summary>
public static class NativeStreamRedirector
{
    private const int StdErrFd = 2;
    private const int OBinary = 0x8000;

    public static void Initialize(Action<string> onNativeLog)
    {
        IntPtr readHandle;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            readHandle = InitializeWindowsPipe();
        }
        else
        {
            readHandle = InitializeLinuxPipe();
        }

        if (readHandle == IntPtr.Zero || readHandle == (IntPtr)(-1))
        {
            return;
        }

        var safeHandle = new SafeFileHandle(readHandle, ownsHandle: true);
        var readStream = new FileStream(safeHandle, FileAccess.Read);
        var reader = new StreamReader(readStream, Encoding.UTF8);

        Task.Factory.StartNew(async () =>
        {
            char[] buffer = new char[2048];
            while (true)
            {
                try
                {
                    int bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string output = new string(buffer, 0, bytesRead);
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            onNativeLog(output.TrimEnd('\n', '\r'));
                        }
                    }
                }
                catch
                {
                    break;
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054")]
    private static IntPtr InitializeWindowsPipe()
    {
        int[] pipeFds = new int[2];
        if (WindowsInterop._pipe(pipeFds, 4096, OBinary) == -1) return IntPtr.Zero;

        WindowsInterop._dup2(pipeFds[1], StdErrFd);
        return WindowsInterop._get_osfhandle(pipeFds[0]);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "SYSLIB1054")]
    private static IntPtr InitializeLinuxPipe()
    {
        int[] pipeFds = new int[2];
        if (LinuxInterop.pipe(pipeFds) == -1) return IntPtr.Zero;

        LinuxInterop.dup2(pipeFds[1], StdErrFd);
        return (IntPtr)pipeFds[0];
    }

    private static class WindowsInterop
    {
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int _pipe(int[] pfds, uint psize, int textmode);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int _dup2(int fd1, int fd2);

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr _get_osfhandle(int fd);
    }

    private static class LinuxInterop
    {
        [DllImport("libc", EntryPoint = "pipe", CallingConvention = CallingConvention.Cdecl)]
        public static extern int pipe(int[] pipefd);

        [DllImport("libc", EntryPoint = "dup2", CallingConvention = CallingConvention.Cdecl)]
        public static extern int dup2(int oldfd, int newfd);
    }
}