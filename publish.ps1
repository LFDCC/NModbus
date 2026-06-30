#Requires -Version 5.1
# ============================================================
#  NModbus 本地打包脚本 (PowerShell 5.1+)
#  用法: .\publish.ps1 -Version <version>
#  示例: .\publish.ps1 -Version 4.0.0
#
#  - 仅做 clean / build / test / pack，不推送
#  - 发布到 NuGet.org 一律通过 push.yml (推送 v*.*.* tag 触发)
#  - 末尾会打印下一步的 git tag / git push 命令
# ============================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "./nupkgs"
)

$ErrorActionPreference = "Stop"
$ProgressPreference   = "SilentlyContinue"

# 形如 vX.Y.Z 或 X.Y.Z；兼容 -rc1 / .beta1 / -preview
if ($Version -notmatch '^[vV]?[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z-]+)?$') {
    Write-Host "错误: 版本号格式非法: '$Version'  (期望形如 4.0.0 或 v4.0.0)" -ForegroundColor Red
    exit 1
}
# 去掉可选的 v 前缀，供 dotnet pack 使用
$TagVersion = $Version -replace '^[vV]', ''

$Projects = @(
    "NModbus/NModbus.csproj",
    "NModbus.Serial/NModbus.Serial.csproj",
    "NModbus.SerialPortStream/NModbus.SerialPortStream.csproj"
)

# 0. dirty 工作区检查 (与 push.yml 的 master 校验对齐)
$gitStatus = git status --porcelain
if ($LASTEXITCODE -eq 0 -and $gitStatus) {
    Write-Host "错误: 工作区有未提交的修改 / 新文件。请先提交或 git stash。" -ForegroundColor Red
    git status --short
    exit 1
}

function Write-Step($n, $total, $msg, $color = "Yellow") {
    Write-Host ""
    Write-Host "[$n/$total] $msg" -ForegroundColor $color
}

function Step-Done($msg) {
    Write-Host "  √ $msg" -ForegroundColor Green
}

function Step-Fail($msg) {
    Write-Host "  × $msg" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  NModbus 本地打包 v$TagVersion" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. 清理
Write-Step 1 5 "清理..."
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
dotnet clean --configuration $Configuration --verbosity quiet . *> $null
Step-Done "清理完成"

# 2. 构建（含所有依赖项目；只编译一次；不重复 restore）
Write-Step 2 5 "构建 ($Configuration)..."
dotnet build --configuration $Configuration --no-restore .
if ($LASTEXITCODE -ne 0) { Step-Fail "构建失败 (exit $LASTEXITCODE)" }
Step-Done "构建成功"

# 3. 测试
Write-Step 3 5 "运行测试..."
dotnet test --configuration $Configuration --no-build --verbosity normal .
if ($LASTEXITCODE -ne 0) { Step-Fail "测试失败 (exit $LASTEXITCODE)" }
Step-Done "测试通过"

# 4. 打包（每个项目单独 pack，避免顶层 Directory.Build.props 干扰 version）
Write-Step 4 5 "打包 NuGet (v$TagVersion)..."
foreach ($proj in $Projects) {
    dotnet pack `
        --configuration $Configuration `
        --no-build `
        --output $OutputDir `
        -p:Version=$TagVersion `
        $proj
    if ($LASTEXITCODE -ne 0) { Step-Fail "$proj 打包失败 (exit $LASTEXITCODE)" }
    $pkgName = [System.IO.Path]::GetFileNameWithoutExtension($proj)
    Step-Done $pkgName
}

# 5. 校验产物
Write-Step 5 5 "校验产物..."
$nupkgs = @(Get-ChildItem -Path $OutputDir -Filter "*.nupkg" -ErrorAction SilentlyContinue)
if (-not $nupkgs -or $nupkgs.Count -eq 0) {
    Step-Fail "未发现任何 .nupkg 文件!"
}
foreach ($pkg in $nupkgs) {
    Write-Host "  √ $($pkg.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  打包完成! v$TagVersion" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  提示: 本脚本不会推送 nuget。请通过 GitHub Actions 发布:" -ForegroundColor White
Write-Host ""
Write-Host "    git tag v$TagVersion" -ForegroundColor Yellow
Write-Host "    git push origin v$TagVersion" -ForegroundColor Yellow
Write-Host ""
Write-Host "  push.yml 会在 ubuntu-latest 上执行 pack + nuget push。" -ForegroundColor Gray
Write-Host "  安装命令:" -ForegroundColor White
Write-Host "    dotnet add package LFDCC.NModbus --version $TagVersion"
Write-Host "    dotnet add package LFDCC.NModbus.Serial --version $TagVersion"
Write-Host ""
