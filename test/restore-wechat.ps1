Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class RestoreWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int t, uint flags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
"@
$found = [IntPtr]::Zero
$cb = [RestoreWin+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][RestoreWin]::GetWindowText($h, $sb, 256)
    if ($sb.ToString() -eq '微信') {
        $script:found = $h
        return $false
    }
    return $true
}
[void][RestoreWin]::EnumWindows($cb, [IntPtr]::Zero)
$result = 'not found'
if ($found -ne [IntPtr]::Zero) {
    [void][RestoreWin]::ShowWindow($found, 9)
    [void][RestoreWin]::SetWindowPos($found, [IntPtr](-1), 40, 120, 1000, 800, 0x0040)
    [void][RestoreWin]::SetForegroundWindow($found)
    $result = 'restored hwnd=' + $found + ' iconic=' + [RestoreWin]::IsIconic($found)
}
Set-Content -LiteralPath 'E:\deepseek1\IslandShot\test\wechat-restore.txt' -Value $result -Encoding UTF8

