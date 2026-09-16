@echo off
chcp 65001 >nul
cd /d "%~dp0src\ProductionManagement.Web"

echo ============================================================
echo   ProductionManagement MES 개발 서버
echo   http://localhost:5206 (이 PC에서만 접속)
echo ============================================================

dotnet run --launch-profile http
