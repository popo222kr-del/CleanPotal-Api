@echo off
chcp 65001 >nul
setlocal EnableExtensions
rem ============================================================================
rem  CleanPotal DB 백업 - 운영 서버에서 더블클릭한다(publish 폴더 안의 "DB백업하기.cmd").
rem
rem  1) 지금 바로 백업: DB 서버 PC 의 백업 폴더에 JUEON_요일.bak 을 만들고 읽히는지 확인한다.
rem  2) 처음 한 번 "매일 자동 백업 등록" 에 Y -> 매일 03:30 에 같은 백업을 한다(작업 이름 CleanPotal DB Backup).
rem
rem  요일별 7개 파일을 돌려 쓰므로 따로 지울 필요 없다. 매월 1일 것은 월별 이름으로 따로 남는다.
rem  결과 기록: C:\Webjueon\publish\App_Data\logs\db-backup.log
rem  복원 방법: docs\db-backup.md
rem  (tools\db-backup.cmd 원본. deploy-test.ps1 이 publish 에 "DB백업하기.cmd" 로 넣는다.)
rem ============================================================================

rem -- 관리자 권한으로 다시 실행(예약 작업 등록에 필요) --
net session >nul 2>&1
if errorlevel 1 (
    echo 관리자 권한이 필요합니다. 확인 창에서 "예" 를 눌러 주세요.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "SITE_DIR=C:\Webjueon\publish"
if not exist "%SITE_DIR%\appsettings.local.json" set "SITE_DIR=%~dp0"
if "%SITE_DIR:~-1%"=="\" set "SITE_DIR=%SITE_DIR:~0,-1%"
set "LOGF=%SITE_DIR%\App_Data\logs\db-backup.log"

title CleanPotal DB 백업
echo.
echo ================== CleanPotal DB 백업 ==================
echo   설정 폴더 : %SITE_DIR%
if not exist "%SITE_DIR%\appsettings.local.json" (
    echo.
    echo [중단] appsettings.local.json 이 없습니다. 운영 서버의 C:\Webjueon\publish 에서 실행하세요.
    goto :end
)
if not exist "%SITE_DIR%\App_Data\logs" mkdir "%SITE_DIR%\App_Data\logs"

echo.
echo [1/2] 백업 중... DB 크기에 따라 몇 분 걸릴 수 있습니다.
pushd "%SITE_DIR%"
echo ---- %date% %time% 수동 실행 >> "%LOGF%"
dotnet CleanPotal.Api.dll backup-db > "%TEMP%\cp-db-backup.txt" 2>&1
set "RC=%errorlevel%"
popd
type "%TEMP%\cp-db-backup.txt" | findstr /c:"[backup]"
type "%TEMP%\cp-db-backup.txt" >> "%LOGF%"
echo.
if not "%RC%"=="0" (
    echo [실패] 위 [backup] 줄을 확인하세요. 전체 기록: %LOGF%
    goto :end
)
echo 백업 성공.

echo.
schtasks /Query /TN "CleanPotal DB Backup" >nul 2>&1
if not errorlevel 1 (
    echo [2/2] 매일 자동 백업이 이미 등록되어 있습니다^(03:30^).
    goto :end
)
if /i not "%SITE_DIR%"=="C:\Webjueon\publish" (
    echo [2/2] 자동 백업 등록은 운영 폴더^(C:\Webjueon\publish^)가 있는 서버에서만 합니다.
    goto :end
)
set /p REG="[2/2] 매일 03:30 자동 백업을 등록할까요? (Y/N) "
if /i not "%REG%"=="Y" goto :end
schtasks /Create /F /TN "CleanPotal DB Backup" /SC DAILY /ST 03:30 /RU SYSTEM /RL HIGHEST /TR "cmd /c cd /d C:\Webjueon\publish && dotnet CleanPotal.Api.dll backup-db >> App_Data\logs\db-backup.log 2>&1"
if errorlevel 1 (echo 등록하지 못했습니다. 위 메시지를 확인하세요.) else (echo 등록했습니다. 매일 03:30 에 백업합니다. 결과는 %LOGF%)

:end
echo.
pause
