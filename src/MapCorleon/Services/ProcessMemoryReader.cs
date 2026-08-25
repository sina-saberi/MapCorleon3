using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MapCorleon.Services;

public interface IProcessMemorySession : IDisposable
{
    float ReadSingle(nint address);
}

public interface IProcessMemoryReader
{
    IProcessMemorySession Open(int processId);
}

public sealed class WindowsProcessMemoryReader : IProcessMemoryReader
{
    public IProcessMemorySession Open(int processId)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccess.VirtualMemoryRead | NativeMethods.ProcessAccess.QueryInformation,
            false,
            processId);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open process {processId} for memory reading.");
        }

        return new WindowsProcessMemorySession(handle);
    }

    private sealed class WindowsProcessMemorySession(SafeProcessHandle handle) : IProcessMemorySession
    {
        public float ReadSingle(nint address)
        {
            var buffer = new byte[sizeof(float)];
            if (!NativeMethods.ReadProcessMemory(handle, address, buffer, (nuint)buffer.Length, out var read) || read != (nuint)buffer.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read process memory at 0x{address:X}.");
            return BitConverter.ToSingle(buffer);
        }

        public void Dispose() => handle.Dispose();
    }

    private static class NativeMethods
    {
        [Flags]
        internal enum ProcessAccess : uint
        {
            VirtualMemoryRead = 0x0010,
            QueryInformation = 0x0400
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(ProcessAccess access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadProcessMemory(
            SafeProcessHandle process,
            nint baseAddress,
            [Out] byte[] buffer,
            nuint size,
            out nuint bytesRead);
    }
}
