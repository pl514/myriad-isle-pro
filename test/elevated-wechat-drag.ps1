param(
    [string]$dir = 'E:\deepseek1\IslandShot\test'
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class HDrag {
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

function Capture([string]$name) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Click([int]$x, [int]$y) {
    [void][HDrag]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 120
    [HDrag]::Down()
    Start-Sleep -Milliseconds 80
    [HDrag]::Up()
}

# 1) expand capsule
Click 960 20
Start-Sleep -Milliseconds 900
Capture 'wechat-panel.png'

# 2) drag first card to WeChat input
$x1 = 575; $y1 = 124; $x2 = 500; $y2 = 760
[void][HDrag]::SetCursorPos($x1, $y1)
Start-Sleep -Milliseconds 250
[HDrag]::Down()
Start-Sleep -Milliseconds 150
$steps = 50
for ($i = 1; $i -le $steps; $i++) {
    $tx = [int]($x1 + ($x2 - $x1) * $i / [double]$steps)
    $ty = [int]($y1 + ($y2 - $y1) * $i / [double]$steps)
    $p = New-Object HDrag+POINT
    [void][HDrag]::GetCursorPos([ref]$p)
    $dx = $tx - $p.X
    $dy = $ty - $p.Y
    if ($dx -ne 0 -or $dy -ne 0) { [HDrag]::mouse_event([HDrag]::MOVE, $dx, $dy, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 20
}
Start-Sleep -Milliseconds 500
Capture 'wechat-drop-before-up.png'
[HDrag]::Up()
Start-Sleep -Milliseconds 1200
Capture 'wechat-after.png'
Set-Content -LiteralPath (Join-Path $dir 'elevated-drag-result.txt') -Value ('done ' + (Get-Date).ToString('HH:mm:ss')) -Encoding UTF8
