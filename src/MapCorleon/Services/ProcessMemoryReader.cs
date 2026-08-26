using System.ComponentModel;
using System.Runtime.InteropServices;
using MapCorleon.Contracts;
using Microsoft.Win32.SafeHandles;

namespace MapCorleon.Services;

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
        public float ReadSingle(nint address) => BitConverter.ToSingle(ReadBytes(address, sizeof(float)));

        public ushort ReadUInt16(nint address) => BitConverter.ToUInt16(ReadBytes(address, sizeof(ushort)));

        public uint ReadUInt32(nint address) => BitConverter.ToUInt32(ReadBytes(address, sizeof(uint)));

        public nint ReadPointer(nint address) => new(BitConverter.ToInt64(ReadBytes(address, sizeof(long))));

        private byte[] ReadBytes(nint address, int length)
        {
            var buffer = new byte[length];
            if (!NativeMethods.ReadProcessMemory(handle, address, buffer, (nuint)buffer.Length, out var read) || read != (nuint)buffer.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not read process memory at 0x{address:X}.");
            return buffer;
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
