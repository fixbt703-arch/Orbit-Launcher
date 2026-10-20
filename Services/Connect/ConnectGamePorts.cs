using System.Runtime.InteropServices;

namespace OrbitLauncher.Services.Connect;

public static class ConnectGamePorts
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    public static bool IsOwned(int port, int pid)
    {
        if (port is < 1 or > 65535 || pid <= 0) return false;
        foreach (var shape in new[] { (Family: 2, Row: 24, Port: 8, Pid: 20), (Family: 23, Row: 56, Port: 20, Pid: 52) })
        {
            int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, shape.Family, 3, 0);
            if (size is < 4 or > 4 * 1024 * 1024) continue;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, shape.Family, 3, 0) != 0) continue;
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (size - 4) / shape.Row) continue;
                for (int i = 0; i < count; i++)
                {
                    int offset = 4 + i * shape.Row;
                    int rawPort = Marshal.ReadInt32(buffer, offset + shape.Port);
                    int actual = ((rawPort & 255) << 8) | ((rawPort >> 8) & 255);
                    if (actual == port && Marshal.ReadInt32(buffer, offset + shape.Pid) == pid) return true;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return false;
    }
}
