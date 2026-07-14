@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================================
echo   CleanPotal 개발 서버 한번에 실행
echo   (백엔드 + 프론트 + cloudflared 터널 - 창 3개가 열립니다)
echo ============================================================

start "CleanPotal-API"    cmd /k "dotnet run --project src\CleanPotal.Api"
start "CleanPotal-Web"    cmd /k "cd client && npm run dev"
start "CleanPotal-Tunnel" cmd /k "cloudflared tunnel --url http://localhost:5173"

echo.
echo 3개 창(API / Web / Tunnel)이 열렸습니다.
echo Tunnel 창에 표시되는 https://...trycloudflare.com 주소를
echo 핸드폰 브라우저에 입력하세요.
echo.
echo (이 창은 닫아도 됩니다. 나머지 3개 창은 켜 두세요.)
pause
