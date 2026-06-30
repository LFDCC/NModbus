@echo off
REM ============================================================
REM  NModbus 本地打包脚本（Windows 双击入口）
REM  用法: 双击运行, 或在 cmd 中 .\publish.cmd 4.0.0
REM
REM  - 内部走 Git Bash 调 publish.sh
REM  - 仅打包, 不推送 nuget (推送请用 git tag v*.*.* + git push)
REM ============================================================

setlocal

set "VERSION=%~1"
if "%VERSION%"=="" (
    set "VERSION=4.0.0"
    echo 未提供版本号, 使用默认: %VERSION%
)

set "BASH="
if exist "C:\Program Files\Git\bin\bash.exe" set "BASH=C:\Program Files\Git\bin\bash.exe"
if "%BASH%"=="" if exist "C:\Program Files (x86)\Git\bin\bash.exe" set "BASH=C:\Program Files (x86)\Git\bin\bash.exe"

if "%BASH%"=="" (
    echo 错误: 未找到 Git Bash (bash.exe)。请安装 Git for Windows。
    exit /b 1
)

"%BASH%" "%~dp0publish.sh" "%VERSION%"
exit /b %ERRORLEVEL%
