# ============================================================
#  NModbus 一键发布脚本 (PowerShell)
#  用法: .\publish.ps1 -Version <version> -ApiKey <key>
#  示例: .\publish.ps1 -Version 2.0.0 -ApiKey "YOUR_API_KEY"
# ============================================================

param(
    [Parameter(Mandatory=$true)]  [string]$Version,
    [Parameter(Mandatory=$true)]  [string]$ApiKey,
    [Parameter(Mandatory=$false)] [string]$Configuration = "Release",
    [Parameter(Mandatory=$false)] [string]$OutputDir = "./nupkgs"
)

$ErrorActionPreference = "Stop"

$Projects = @(
    "NModbus/NModbus.csproj",
    "NModbus.Serial/NModbus.Serial.csproj",
    "NModbus.SerialPortStream/NModbus.SerialPortStream.csproj"
)

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  NModbus 发布 v$Version" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 清理
Write-Host "[1/5] 清理..." -ForegroundColor Yellow
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
dotnet clean --configuration $Configuration . -q 2>$null
Write-Host "  √ 清理完成" -ForegroundColor Green

# 2. 构建
Write-Host "[2/5] 构建 ($Configuration)..." -ForegroundColor Yellow
dotnet build --configuration $Configuration . -q
if ($LASTEXITCODE -ne 0) { Write-Host "  × 构建失败" -ForegroundColor Red; exit 1 }
Write-Host "  √ 构建成功" -ForegroundColor Green

# 3. 测试
Write-Host "[3/5] 运行测试..." -ForegroundColor Yellow
dotnet test --configuration $Configuration --no-build . -q
if ($LASTEXITCODE -ne 0) { Write-Host "  × 测试失败" -ForegroundColor Red; exit 1 }
Write-Host "  √ 测试通过" -ForegroundColor Green

# 4. 打包
Write-Host "[4/5] 打包 NuGet (v$Version)..." -ForegroundColor Yellow
foreach ($proj in $Projects) {
    dotnet pack --configuration $Configuration -p:Version=$Version --output $OutputDir $proj -q
    $pkgName = [System.IO.Path]::GetFileNameWithoutExtension($proj)
    Write-Host "  √ $pkgName" -ForegroundColor Green
}

# 5. 发布
Write-Host "[5/5] 发布到 NuGet.org..." -ForegroundColor Yellow
$nupkgs = Get-ChildItem -Path $OutputDir -Filter "*.nupkg"

if (-not $nupkgs -or $nupkgs.Count -eq 0) {
    Write-Host "  × 未找到 .nupkg 文件！请检查打包步骤。" -ForegroundColor Red
    exit 1
}

foreach ($pkg in $nupkgs) {
    Write-Host "  推送 $($pkg.Name) ..." -ForegroundColor Gray
    dotnet nuget push $pkg.FullName --api-key $ApiKey --source https://api.nuget.org/v3/index.json --skip-duplicate
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  × $($pkg.Name) 发布失败 (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit 1
    }
    Write-Host "  √ $($pkg.Name)" -ForegroundColor Green
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  发布完成! v$Version" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  安装命令:" -ForegroundColor White
Write-Host "    dotnet add package LFDCC.NModbus --version $Version"
Write-Host "    dotnet add package LFDCC.NModbus.Serial --version $Version"
Write-Host ""
