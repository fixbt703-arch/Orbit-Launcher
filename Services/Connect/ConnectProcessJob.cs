using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OrbitLauncher.Services.Connect;

// A worker crash must not leave a world published by an orphaned helper.
internal sealed class ConnectProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    public ConnectProcessJob(Process process)
    {
        _handle = CreateJobObjectW(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr info = Marshal.AllocHGlobal(144);
        try
        {
            Marshal.Copy(new byte[144], 0, info, 144); Marshal.WriteInt32(info, 16, 0x2000);
            if (!SetInformationJobObject(_handle, 9, info, 144) || !AssignProcessToJobObject(_handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { _handle.Dispose(); throw; }
        finally { Marshal.FreeHGlobal(info); }
    }
    public void Dispose() => _handle.Dispose();
}
