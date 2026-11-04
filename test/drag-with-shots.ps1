param(
    [string]$outDir = 'E:\deepseek1\IslandShot\test'
)

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DragShot {
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

function Capture-Screen([string]$path) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Add-Type -AssemblyName System.Windows.Forms
$x1 = 555; $y1 = 108
$x2 = 1440; $y2 = 620

[void][DragShot]::SetCursorPos($x1, $y1)
Start-Sleep -Milliseconds 250
[DragShot]::Down()
Start-Sleep -Milliseconds 150

$steps = 45
for ($i = 1; $i -le $steps; $i++) {
    $tx = [int]($x1 + ($x2 - $x1) * $i / [double]$steps)
    $ty = [int]($y1 + ($y2 - $y1) * $i / [double]$steps)
    $p = New-Object DragShot+POINT
    [void][DragShot]::GetCursorPos([ref]$p)
    $dx = $tx - $p.X
    $dy = $ty - $p.Y
    if ($dx -ne 0 -or $dy -ne 0) { [DragShot]::mouse_event([DragShot]::MOVE, $dx, $dy, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 20
    if ($i -eq 22) { Capture-Screen (Join-Path $outDir 'mid-drag-preview.png') }
}

Start-Sleep -Milliseconds 350
Capture-Screen (Join-Path $outDir 'drop-target-hint.png')
Start-Sleep -Milliseconds 250
[DragShot]::Up()
Start-Sleep -Milliseconds 400
Write-Output 'done'
