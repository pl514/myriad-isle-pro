$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null

# csc 会在 %TEMP% 里建中间文件；沙箱/受限环境的临时目录可能不可写，统一指到项目内
$scratch = Join-Path $root '.tmp'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$env:TMP = $scratch
$env:TEMP = $scratch

# 抖音小窗用的 WebView2 SDK（随仓库一起放在 vendor\webview2，免安装、免下载）
$webview2 = Join-Path $root 'vendor\webview2'
if (-not (Test-Path (Join-Path $webview2 'Microsoft.Web.WebView2.Wpf.dll'))) {
    throw "缺少 WebView2 SDK：$webview2"
}

$refs = @(
    "/r:$framework\WPF\PresentationCore.dll",
    "/r:$framework\WPF\PresentationFramework.dll",
    "/r:$framework\WPF\WindowsBase.dll",
    "/r:$framework\System.Xaml.dll",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:$framework\System.Web.Extensions.dll",
    "/r:System.Numerics.dll",
    "/r:$framework\System.Net.Http.dll",
    "/r:$framework\System.Security.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:$webview2\Microsoft.Web.WebView2.Core.dll",
    "/r:$webview2\Microsoft.Web.WebView2.Wpf.dll"
)
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    "/out:$out\MyriadIslePro.exe" `
    "/win32manifest:$root\src\app.manifest" `
    "/win32icon:$root\src\app.ico" `
    $refs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# 运行期必须和 exe 放在一起的三个文件
Copy-Item (Join-Path $webview2 'Microsoft.Web.WebView2.Core.dll') $out -Force
Copy-Item (Join-Path $webview2 'Microsoft.Web.WebView2.Wpf.dll') $out -Force
Copy-Item (Join-Path $webview2 'WebView2Loader.dll') $out -Force

# 投射窗的本地网页（WebView2 虚拟主机指向这个目录）
$stageWeb = Join-Path $root 'stage-web'
if (Test-Path $stageWeb) {
    $stageOut = Join-Path $out 'stage-web'
    if (Test-Path $stageOut) { Remove-Item $stageOut -Recurse -Force }
    New-Item -ItemType Directory -Path $stageOut -Force | Out-Null
    Copy-Item (Join-Path $stageWeb '*') $stageOut -Recurse -Force
}

Write-Output "Built: $out\MyriadIslePro.exe"
