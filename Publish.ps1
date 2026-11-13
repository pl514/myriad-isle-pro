# 万象岛 Pro 发布打包脚本
# 用法：  .\Publish.ps1            （版本号自动从 src\AppInfo.cs 读取）
#         .\Publish.ps1 -Version 3.21
# 产物：  publish\万象岛-Pro-<版本>\  和  publish\万象岛-Pro-<版本>.zip
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# ---- 版本号 ----
if (-not $Version) {
    $info = Join-Path $root 'src\AppInfo.cs'
    if (Test-Path $info) {
        $match = Select-String -Path $info -Pattern 'Version\s*=\s*"([0-9\.]+)"' | Select-Object -First 1
        if ($match) { $Version = $match.Matches[0].Groups[1].Value }
    }
    if (-not $Version) { $Version = '0.0' }
}

$package = "万象岛-Pro-$Version"
$outRoot = Join-Path $root 'publish'
$stage = Join-Path $outRoot $package
$zip = Join-Path $outRoot ($package + '.zip')
$build = Join-Path $root 'build'

Write-Host "== 打包 $package ==" -ForegroundColor Cyan

# ---- 1. 编译 ----
& (Join-Path $root 'Build.ps1')
$exe = Join-Path $build 'MyriadIslePro.exe'
if (-not (Test-Path $exe)) { throw "编译产物不存在：$exe" }

# ---- 2. 清掉开发期日志（不随包发布）----
$dataDir = Join-Path $build 'data'
if (Test-Path $dataDir) {
    Get-ChildItem $dataDir -File -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
}

# ---- 3. 组包 ----
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $exe $stage -Force
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll')) {
    $src = Join-Path $build $dll
    if (Test-Path $src) { Copy-Item $src $stage -Force }
}
$stageWeb = Join-Path $build 'stage-web'
if (Test-Path $stageWeb) { Copy-Item $stageWeb $stage -Recurse -Force }

# ---- 4. 使用说明 ----
$readme = @'
万象岛 Pro —— Windows 上的动态岛胶囊截图 / 翻译 / 音乐 / 抖音小窗
版本：__VERSION__

一、运行
  双击 MyriadIslePro.exe 即可（免安装）。
  首次运行会在程序目录下创建 data\ 保存图片、文字、配置和缓存。

  如果双击没反应：多为杀毒软件拦截未签名程序，把整个文件夹加入信任后重试。

二、功能速览
  · 截图：区域 / 窗口 / 全屏，图片进入胶囊，右键可编辑、复制、钉在桌面
  · 图片编辑：矩形、椭圆、箭头、画笔、高亮、文字、气泡、步骤、模糊、马赛克、裁剪……
             保存 = 覆盖原图；另存为 = 生成新文件
  · 文字：剪贴板文字自动收进胶囊，可复制、清空
  · 翻译：文本翻译（自动 / 免费 MyMemory / 自定义 AI 模型）
         屏幕翻译：热键 → 框选区域 → 只翻译框内内容
  · 抖音小窗：点三点球里的抖音球，网页版抖音在独立小窗里刷
  · 音乐面板：播放、歌词投射到桌面
  · 微信 / QQ：收到消息时胶囊右侧提示，点提示跳转客户端
  · 屏幕录制：ffmpeg 录制 MP4（需系统装有 ffmpeg，或把 ffmpeg.exe 放到本目录）

三、热键（全部可改：托盘右键 → 热键设置）
  显示 / 收起胶囊、区域截图、窗口截图、全屏截图、
  图片收起、开始停止录屏、抖音小窗、屏幕翻译

四、数据位置
  全部在程序目录下的 data\：
    images\        截图与图片
    screenshots\   剪贴板图片
    Recordings\    录屏
    translate-img\ 图片翻译结果
  想迁移：整个文件夹复制走即可（绿色版）。

五、说明
  · 本程序不开源分发第三方内容，抖音 / 微信 / QQ 均为本机客户端窗口的调用与展示。
  · 免费翻译走 MyMemory，量大时可能限流；可在「翻译 → 引擎」里换成自己的 AI 模型。

__VERSION__
'@
$readme = $readme.Replace('__VERSION__', $Version)
$readmePath = Join-Path $stage '使用说明.txt'
[System.IO.File]::WriteAllText($readmePath, $readme, (New-Object System.Text.UTF8Encoding($true)))

# ---- 5. 压缩 ----
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Write-Host "  文件夹  $stage"
Write-Host "  压缩包  $zip  ($size MB)"
Write-Host ""
Get-ChildItem $stage | Select-Object Name, @{n='大小(KB)';e={ if ($_.PSIsContainer) { '<目录>' } else { [math]::Round($_.Length/1KB,0) } }} | Format-Table -AutoSize
