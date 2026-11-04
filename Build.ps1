$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$refs = @(
    "/r:$framework\WPF\PresentationCore.dll",
    "/r:$framework\WPF\PresentationFramework.dll",
    "/r:$framework\WPF\WindowsBase.dll",
    "/r:$framework\System.Xaml.dll",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll"
)
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$out\Capsule image Pro.exe" `
    "/win32manifest:$root\src\app.manifest" `
    "/win32icon:$root\src\app.ico" `
    $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

Write-Output "Built: $out\Capsule image Pro.exe"
