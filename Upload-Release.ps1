# 万象岛 Pro 发布上传脚本（推送代码 + 建 Release + 上传 zip）
#
# 用法（在你网络能连上 github.com 的时候跑）：
#   $env:GH_TOKEN = "github_pat_xxx"       # 只需一次，建议用 fine-grained token：
#                                          #   Repo: pl514/capsule-image-pro
#                                          #   Permissions: Contents = Read and write
#   .\Upload-Release.ps1
#
# 可选参数：
#   -Version 3.21        指定版本（默认读 src\AppInfo.cs）
#   -MakePublic          顺手把仓库改成公开（不可逆，慎重）
#   -Proxy 127.0.0.1:7890 走本地代理推送
#   -DryRun              只打印将要做什么，不真的发请求
param(
    [string]$Version = "",
    [string]$Token = "",
    [switch]$MakePublic,
    [string]$Proxy = "",
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# ---------- 版本 / 仓库 ----------
if (-not $Version) {
    $info = Join-Path $root 'src\AppInfo.cs'
    if (Test-Path $info) {
        $m = Select-String -Path $info -Pattern 'Version\s*=\s*"([0-9\.]+)"' | Select-Object -First 1
        if ($m) { $Version = $m.Matches[0].Groups[1].Value }
    }
    if (-not $Version) { throw '读不到版本号，用 -Version 指定' }
}

$remote = (& git remote get-url origin).Trim()
if ($remote -notmatch 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\.]+)') { throw "解析不了 remote：$remote" }
$owner = $Matches['owner']
$repo = $Matches['repo']
$tag = "v$Version"
$zip = Join-Path $root "publish\万象岛-Pro-$Version.zip"

if (-not (Test-Path $zip)) { throw "找不到发布包：$zip`n先跑 .\Publish.ps1" }

# ---------- Token ----------
if (-not $Token) {
    if ($env:GH_TOKEN) { $Token = $env:GH_TOKEN }
    elseif ($env:GITHUB_TOKEN) { $Token = $env:GITHUB_TOKEN }
}
if (-not $Token -and -not $DryRun) {
    $secure = Read-Host -AsSecureString "输入 GitHub Token（fine-grained，Contents 读写）"
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $Token = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
if (-not $Token -and -not $DryRun) { throw '没有 Token，无法创建 Release' }

$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'MyriadIsle-Release'
}

function Step($text) { Write-Host "  → $text" -ForegroundColor Cyan }

# ---------- 0. 代理（可选） ----------
if ($Proxy) {
    $env:HTTPS_PROXY = "http://$Proxy"
    $env:HTTP_PROXY = "http://$Proxy"
    & git config --global http.proxy "http://$Proxy"
    & git config --global https.proxy "http://$Proxy"
    Step "git 走代理 $Proxy"
}

Write-Host "== 发布 $owner/$repo  $tag ==" -ForegroundColor Green

# ---------- 1. 推送代码 + 标签 ----------
Step 'git push origin main'
if ($DryRun) { Write-Host '     [DryRun] 跳过' } else {
    & git push origin main
    if ($LASTEXITCODE -ne 0) { throw 'git push 失败：检查网络 / 代理 / 凭据（GitHub 只接受 token 或 SSH，不再接受密码）' }
}

Step 'git push origin --tags'
if ($DryRun) { Write-Host '     [DryRun] 跳过' } else {
    & git push origin --tags
    if ($LASTEXITCODE -ne 0) { Write-Host '     （标签推送有冲突可忽略，Release 里会用 -Tag 自动建）' -ForegroundColor Yellow }
}

# ---------- 2. 建 Release ----------
$releaseUrl = "https://api.github.com/repos/$owner/$repo/releases"
$body = @{
    tag_name   = $tag
    name       = "万象岛 Pro $Version"
    body       = "万象岛 Pro $Version 发布`n`n- 见 README 与 使用说明.txt"
    draft      = $false
    prerelease = $false
} | ConvertTo-Json -Depth 4

Step "创建 Release $tag"
$release = $null
if ($DryRun) { Write-Host '     [DryRun] 跳过' } else {
    try {
        $release = Invoke-RestMethod -Uri $releaseUrl -Method Post -Headers $headers -Body $body -ContentType 'application/json'
        Write-Host "     Release: $($release.html_url)" -ForegroundColor Green
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match '422') {
            Step '该 tag 已有 Release，尝试取现有 Release 并覆盖附件'
            $existing = Invoke-RestMethod -Uri "$releaseUrl/tags/$tag" -Headers $headers
            $release = $existing
        } else { throw }
    }
}

# ---------- 3. 上传 zip ----------
if ($release) {
    $assets = Invoke-RestMethod -Uri "$($release.upload_url -replace '\{.*\}','')" -Headers $headers -Method Get
    $name = "MyriadIslePro-$Version.zip"   # 附件名用英文：中文名在 URL 编码里会被吃掉
    foreach ($a in $assets) {
        if ($a.name -eq $name) {
            Step "删除同名旧附件 $name"
            Invoke-RestMethod -Uri "$releaseUrl/assets/$($a.id)" -Headers $headers -Method Delete | Out-Null
        }
    }

    Step "上传 $name（$([math]::Round((Get-Item $zip).Length/1MB,2)) MB）"
    $uploadUrl = $release.upload_url -replace '\{.*\}', ''
    $upHeaders = @{
        Authorization = "Bearer $Token"
        Accept        = 'application/vnd.github+json'
        'User-Agent'  = 'MyriadIsle-Release'
    }
    $result = Invoke-RestMethod -Uri "$uploadUrl?name=$name" -Method Post -Headers $upHeaders `
        -ContentType 'application/zip' -InFile $zip
    Write-Host "     附件：$($result.browser_download_url)" -ForegroundColor Green
}

# ---------- 4. 可选：仓库转公开 ----------
if ($MakePublic) {
    Step '把仓库改成公开（不可逆）'
    if ($DryRun) { Write-Host '     [DryRun] 跳过' } else {
        $patch = @{ private = $false } | ConvertTo-Json
        Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo" -Method Patch -Headers $headers -Body $patch -ContentType 'application/json' | Out-Null
        Write-Host "     已公开：https://github.com/$owner/$repo" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host '完成。' -ForegroundColor Green
Write-Host "  仓库   https://github.com/$owner/$repo"
Write-Host "  Release https://github.com/$owner/$repo/releases/tag/$tag"
