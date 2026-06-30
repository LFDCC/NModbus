#!/bin/bash
# ============================================================
#  NModbus 本地打包脚本
#  用法: ./publish.sh <version>
#  示例: ./publish.sh 4.0.0
#
#  - 仅做 clean / build / test / pack，不推送
#  - 发布到 NuGet.org 一律通过 push.yml (推送 v*.*.* tag 触发)
#  - 末尾会打印下一步的 git tag / git push 命令
# ============================================================

set -euo pipefail

VERSION=${1:-}
if [[ -z "${VERSION}" ]]; then
    echo "错误: 请提供版本号, 例如: ./publish.sh 4.0.0"
    exit 1
fi

# 形如 vX.Y.Z 或 X.Y.Z 都可以，但不接受空
if ! [[ "${VERSION}" =~ ^[vV]?[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z-]+)?$ ]]; then
    echo "错误: 版本号格式非法: '${VERSION}'  (期望形如 4.0.0 或 v4.0.0)"
    exit 1
fi
# 内部统一去掉 v 前缀，供 dotnet pack 使用
TAG_VERSION="${VERSION#v}"
TAG_VERSION="${TAG_VERSION#V}"

CONFIGURATION="Release"
OUTPUT_DIR="./nupkgs"

PROJECTS=(
    "NModbus/NModbus.csproj"
    "NModbus.Serial/NModbus.Serial.csproj"
    "NModbus.SerialPortStream/NModbus.SerialPortStream.csproj"
)

echo "=========================================="
echo "  NModbus 本地打包 v${TAG_VERSION}"
echo "=========================================="
echo ""

# 0. 跳过 dirty 工作区（与 push.yml 行为保持一致）
if ! git diff --quiet --ignore-submodules HEAD 2>/dev/null \
   || ! git diff --quiet --ignore-submodules --cached HEAD 2>/dev/null \
   || [[ -n "$(git ls-files --others --exclude-standard 2>/dev/null)" ]]; then
    echo "错误: 工作区有未提交的修改 / 新文件。请先提交或 git stash。" >&2
    git status --short
    exit 1
fi

# 1. 清理
echo "[1/5] 清理..."
rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"
dotnet clean --configuration "${CONFIGURATION}" . --verbosity quiet 2>/dev/null || true
echo "  √ 清理完成"

# 2. 构建（含所有依赖项目；只编译一次）
echo "[2/5] 构建 (${CONFIGURATION})..."
dotnet build --configuration "${CONFIGURATION}" --no-restore .
echo "  √ 构建成功"

# 3. 测试
echo "[3/5] 运行测试..."
dotnet test --configuration "${CONFIGURATION}" --no-build --verbosity normal .
echo "  √ 测试通过"

# 4. 打包（每个项目单独 pack，避免顶层 Directory.Build.props 干扰 version）
echo "[4/5] 打包 NuGet (v${TAG_VERSION})..."
for proj in "${PROJECTS[@]}"; do
    dotnet pack \
        --configuration "${CONFIGURATION}" \
        --no-build \
        --output "${OUTPUT_DIR}" \
        -p:Version="${TAG_VERSION}" \
        "${proj}"
    pkg_name=$(basename "${proj}" .csproj)
    echo "  √ ${pkg_name}"
done

# 5. 校验产物
echo "[5/5] 校验产物..."
shopt -s nullglob
pkgs=("${OUTPUT_DIR}"/*.nupkg)
shopt -u nullglob
if [[ ${#pkgs[@]} -eq 0 ]]; then
    echo "  × 未发现任何 .nupkg 文件！" >&2
    exit 1
fi
for p in "${pkgs[@]}"; do
    echo "  √ $(basename "${p}")"
done

echo ""
echo "=========================================="
echo "  打包完成! v${TAG_VERSION}"
echo "=========================================="
echo ""
echo "  提示: 本脚本不会推送 nuget。请通过 GitHub Actions 发布："
echo ""
echo "    git tag v${TAG_VERSION}"
echo "    git push origin v${TAG_VERSION}"
echo ""
echo "  push.yml 会在 ubuntu-latest 上执行 pack + nuget push。"
echo "  安装命令:"
echo "    dotnet add package LFDCC.NModbus --version ${TAG_VERSION}"
echo "    dotnet add package LFDCC.NModbus.Serial --version ${TAG_VERSION}"
echo ""
