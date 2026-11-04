Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class IL {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool i, uint p);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError=true)] public static extern bool GetTokenInformation(IntPtr t, int c, IntPtr buf, int len, out int ret);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

    public static string Check(uint pid) {
        IntPtr p = OpenProcess(0x1000, false, pid);
        if (p == IntPtr.Zero) return "open fail " + Marshal.GetLastWin32Error();
        IntPtr t;
        if (!OpenProcessToken(p, 8, out t)) {
            int e = Marshal.GetLastWin32Error();
            CloseHandle(p);
            return "token deny " + e;
        }
        IntPtr buf = Marshal.AllocHGlobal(256);
        int ret;
        string result;
        if (!GetTokenInformation(t, 25, buf, 256, out ret)) {
            result = "il fail " + Marshal.GetLastWin32Error();
        } else {
            IntPtr sid = Marshal.ReadIntPtr(buf, 0);
            int count = Marshal.ReadByte(sid, 1);
            int off = 8 + 4 * (count - 1);
            int level = Marshal.ReadInt32(sid, off);
            string name = level == 0x1000 ? "Low" : level == 0x2000 ? "Medium" : level == 0x3000 ? "High" : level == 0x4000 ? "System" : "0x" + level.ToString("X");
            result = "IL=" + name + " (0x" + level.ToString("X") + ") sidCount=" + count;
        }
        Marshal.FreeHGlobal(buf);
        CloseHandle(t);
        CloseHandle(p);
        return result;
    }
}
"@

$names = @('IslandShot', 'DropProbe', 'Weixin')
foreach ($n in $names) {
    $p = Get-Process $n -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) {
        Write-Output ("{0} ({1}) : {2}" -f $n, $p.Id, [IL]::Check([uint32]$p.Id))
    } else {
        Write-Output ("{0}: not running" -f $n)
    }
}
Write-Output ("self ({0}) : {1}" -f $PID, [IL]::Check([uint32]$PID))
