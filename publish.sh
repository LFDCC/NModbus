#!/bin/bash
# ============================================================
#  NModbus 一键发布脚本
#  用法: ./publish.sh <version> [nuget-api-key]
#  示例: ./publish.sh 2.0.0 YOUR_API_KEY
# ============================================================

set -e

VERSION=${1:? "错误: 请提供版本号, 例如: ./publish.sh 2.0.0"}
API_KEY=${2:? "错误: 请提供 NuGet API Key, 例如: ./publish.sh 2.0.0 oy2xxxx..."}
CONFIGURATION="Release"
OUTPUT_DIR="./nupkgs"

PROJECTS=(
    "NModbus/NModbus.csproj"
    "NModbus.Serial/NModbus.Serial.csproj"
    "NModbus.SerialPortStream/NModbus.SerialPortStream.csproj"
)

echo "=========================================="
echo "  NModbus 发布 v${VERSION}"
echo "=========================================="
echo ""

# 1. 清理
echo "[1/5] 清理..."
rm -rf ${OUTPUT_DIR}
mkdir -p ${OUTPUT_DIR}
dotnet clean --configuration ${CONFIGURATION} . -q 2>/dev/null || true
echo "  ✓ 清理完成"

# 2. 构建
echo "[2/5] 构建 (${CONFIGURATION})..."
dotnet build --configuration ${CONFIGURATION} . -q
echo "  ✓ 构建成功"

# 3. 测试
echo "[3/5] 运行测试..."
dotnet test --configuration ${CONFIGURATION} --no-build . -q
echo "  ✓ 测试通过"

# 4. 打包
echo "[4/5] 打包 NuGet (v${VERSION})..."
for proj in "${PROJECTS[@]}"; do
    dotnet pack --configuration ${CONFIGURATION} -p:Version=${VERSION} --output ${OUTPUT_DIR} ${proj} -q
    pkg_name=$(basename ${proj} .csproj)
    echo "  ✓ ${pkg_name}"
done

# 5. 发布
echo "[5/5] 发布到 NuGet.org..."
for pkg in ${OUTPUT_DIR}/*.nupkg; do
    dotnet nuget push "${pkg}" --api-key ${API_KEY} --source https://api.nuget.org/v3/index.json --skip-duplicate 2>&1 | grep -v "^$" || true
    echo "  ✓ $(basename ${pkg})"
done

echo ""
echo "=========================================="
echo "  发布完成! v${VERSION}"
echo "=========================================="
echo ""
echo "  安装命令:"
echo "    dotnet add package LFDCC.NModbus --version ${VERSION}"
echo "    dotnet add package LFDCC.NModbus.Serial --version ${VERSION}"
echo ""
