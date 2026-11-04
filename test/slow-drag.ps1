param(
    [int]$x1 = 555,
    [int]$y1 = 108,
    [int]$x2 = 1440,
    [int]$y2 = 620,
    [int]$holdMs = 4000
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class MoveDrag {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
    public const uint MOVE = 0x0001;
    public static void Down() { mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); }
    public static void Up() { mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero); }
}
"@

[void][MoveDrag]::SetCursorPos($x1, $y1)
Start-Sleep -Milliseconds 250
[MoveDrag]::Down()
Start-Sleep -Milliseconds 150

$steps = 60
for ($i = 1; $i -le $steps; $i++) {
    $tx = [int]($x1 + ($x2 - $x1) * $i / [double]$steps)
    $ty = [int]($y1 + ($y2 - $y1) * $i / [double]$steps)
    $p = New-Object MoveDrag+POINT
    [void][MoveDrag]::GetCursorPos([ref]$p)
    $dx = $tx - $p.X
    $dy = $ty - $p.Y
    if ($dx -ne 0 -or $dy -ne 0) { [MoveDrag]::mouse_event([MoveDrag]::MOVE, $dx, $dy, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 22
}

Start-Sleep -Milliseconds $holdMs
[MoveDrag]::Up()
