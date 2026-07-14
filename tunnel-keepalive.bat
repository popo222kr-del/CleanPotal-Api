@echo off
chcp 65001 >nul

REM cloudflared 무료 터널을 자동 재시작하는 스크립트.
REM 터널이 끊겨서 종료되면 5초 뒤 자동으로 다시 연결합니다.
REM (재시작 시 https 주소가 새로 바뀌므로, 바뀐 주소를 핸드폰에 다시 입력)

:loop
echo.
echo === cloudflared 터널 시작 (%date% %time%) ===
cloudflared tunnel --url http://localhost:5173
echo.
echo !! 터널이 종료되었습니다. 5초 후 자동 재시작합니다. 주소가 바뀔 수 있습니다. !!
timeout /t 5 /nobreak >nul
goto loop
