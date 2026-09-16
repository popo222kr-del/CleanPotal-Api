@echo off
chcp 65001 >nul
cd /d "%~dp0src\ProductionManagement.Web"

echo ============================================================
echo   ProductionManagement MES 개발 서버
echo   http://localhost:5206 (이 PC에서만 접속)
echo ============================================================

REM dotnet watch = 코드를 고치거나 git pull 하면 스스로 다시 빌드해서 뜬다.
dotnet watch run --launch-profile http
