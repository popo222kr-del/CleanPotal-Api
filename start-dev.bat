@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================================
echo   CleanPotal 개발 서버 한번에 실행
echo   (백엔드 + 프론트 + MES + cloudflared 터널 - 창 4개가 열립니다)
echo.
echo   코드를 고치거나 git pull 하면 알아서 다시 뜹니다.
echo   (.csproj / 패키지 / .NET 버전을 바꿨을 때만 창을 닫고 다시 실행)
echo ============================================================

REM 온·습도 구독은 끈다 — 개발 PC 설정도 운영 DB 를 가리키므로, 켜 두면 운영 포털과 함께 같은 센서 값을
REM 운영 DB 에 두 번 적는다(테스트 서버와 같은 이유). 개발 모드에서 수집을 시험할 때만 이 줄을 지운다.
set "Zigbee__Mqtt__Enabled=false"

REM dotnet watch = C# 를 고치거나 git pull 하면 스스로 다시 빌드해서 뜬다.
REM (React 는 Vite 가 이미 그렇게 동작한다 — 저장하면 브라우저에 바로 반영)
start "CleanPotal-API"    cmd /k "dotnet watch --project src\CleanPotal.Api run"
start "CleanPotal-Web"    cmd /k "cd client && npm run dev"
start "CleanPotal-MES"    cmd /k "call mes\start-mes.bat"
start "CleanPotal-Tunnel" cmd /k "cloudflared tunnel --url http://localhost:5173"

echo.
echo 4개 창(API / Web / MES / Tunnel)이 열렸습니다.
echo Tunnel 창에 표시되는 https://...trycloudflare.com 주소를
echo 핸드폰 브라우저에 입력하세요.
echo.
echo (이 창은 닫아도 됩니다. 나머지 3개 창은 켜 두세요.)
echo 코드 변경은 자동 반영됩니다 - 브라우저 새로고침만 하세요.
pause
