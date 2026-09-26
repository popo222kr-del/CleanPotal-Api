@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
rem ============================================================================
rem  CleanPotal 운영 배포 - 운영 서버에서 publish 폴더 안의 이 파일을 더블클릭한다.
rem
rem  1) 개발 PC 의 publish 폴더를 원격 데스크톱으로 운영 서버 아무 곳(예: 바탕화면)에 통째로 복사
rem  2) 복사한 폴더 안의 "배포하기.cmd" 더블클릭 -> 관리자 권한 "예"
rem
rem  하는 일: 점검 안내 화면 켜기 -> 앱 풀 중지 -> 지금 버전 백업 -> 보존 파일을 뺀 나머지를
rem           이 폴더 내용으로 교체 -> 앱 풀 시작 -> 점검 안내 끄기 -> 포털 깨우기 -> 결과 확인
rem  보존(건드리지 않음): appsettings.local.json(+.before-mqtt), App_Data, cleanpotal*.db, package-lock.json
rem  되돌리기: C:\Webjueon\backup\날짜_시각 폴더 안의 "배포하기.cmd" 를 더블클릭하면 그 버전으로 돌아간다.
rem  (tools\prod-deploy.cmd 원본. deploy-test.ps1 이 publish 에 "배포하기.cmd" 로 넣는다.)
rem ============================================================================

set "SITE_DIR=C:\Webjueon\publish"
set "BACKUP_DIR=C:\Webjueon\backup"
set "POOL=Cleanjueon"
set "SITE_URL=http://10.10.10.119:8713/"
set "APPCMD=%windir%\system32\inetsrv\appcmd.exe"
set "SRC=%~dp0"
set "SRC=%SRC:~0,-1%"

rem -- 관리자 권한으로 다시 실행 --
net session >nul 2>&1
if errorlevel 1 (
    echo 관리자 권한이 필요합니다. 확인 창에서 "예" 를 눌러 주세요.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

title CleanPotal 운영 배포
echo.
echo ================== CleanPotal 운영 배포 ==================
echo   가져올 폴더 : %SRC%
echo   운영 폴더   : %SITE_DIR%

if not exist "%SRC%\CleanPotal.Api.dll" (
    echo.
    echo [중단] 이 폴더에 CleanPotal.Api.dll 이 없습니다. publish 폴더 안에서 실행하세요.
    goto :end
)
if /i "%SRC%"=="%SITE_DIR%" (
    echo.
    echo [중단] 운영 폴더 안에서 실행했습니다. 복사해 온 publish 폴더에서 실행하세요.
    goto :end
)
echo.
echo   새 버전:
if exist "%SRC%\build-info.json" (type "%SRC%\build-info.json") else (echo   ^(build-info.json 없음^))
echo.
echo   지금 운영 버전:
if exist "%SITE_DIR%\build-info.json" (type "%SITE_DIR%\build-info.json") else (echo   ^(정보 없음^))
echo.
set /p OK="배포할까요? (Y/N) "
if /i not "%OK%"=="Y" ( echo 취소했습니다. & goto :end )

for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "STAMP=%%t"
set "BK=%BACKUP_DIR%\%STAMP%"
set "EXCL_F=appsettings.local.json appsettings.local.json.before-mqtt cleanpotal.db cleanpotal.generated-20260921.db.db package-lock.json app_offline.htm 배포하기.cmd"

echo.
echo [1/6] 점검 안내 화면 켜기(app_offline.htm)
> "%SITE_DIR%\app_offline.htm" echo ^<meta charset="utf-8"^>^<title^>점검 중^</title^>^<body style="font-family:sans-serif;text-align:center;padding-top:80px"^>^<h2^>업데이트 중입니다^</h2^>^<p^>1~2분 뒤 새로고침해 주세요.^</p^>^</body^>
timeout /t 3 /nobreak >nul

echo [2/6] 앱 풀 중지(%POOL%)
"%APPCMD%" stop apppool /apppool.name:%POOL% >nul 2>&1
timeout /t 5 /nobreak >nul

echo [3/6] 지금 버전 백업 -^> %BK%
robocopy "%SITE_DIR%" "%BK%" /E /XD App_Data /XF %EXCL_F% /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul
copy /y "%~f0" "%BK%\배포하기.cmd" >nul
rem 백업은 최근 5개만 남긴다
for /f "skip=5 delims=" %%d in ('dir /b /ad /o-n "%BACKUP_DIR%" 2^>nul') do rd /s /q "%BACKUP_DIR%\%%d"

echo [4/6] 교체(보존 파일은 그대로)
robocopy "%SRC%" "%SITE_DIR%" /MIR /XD App_Data /XF %EXCL_F% /R:3 /W:3 /NFL /NDL /NJH /NP
if errorlevel 8 (
    echo.
    echo [오류] 복사 중 실패했습니다. 위 메시지를 확인하세요. 되돌리려면 %BK%\배포하기.cmd 를 실행하세요.
    "%APPCMD%" start apppool /apppool.name:%POOL% >nul 2>&1
    goto :end
)

echo [5/6] 앱 풀 시작, 점검 안내 끄기
"%APPCMD%" start apppool /apppool.name:%POOL% >nul 2>&1
del /q "%SITE_DIR%\app_offline.htm" >nul 2>&1

echo [6/6] 포털 깨우기, 결과 확인
timeout /t 5 /nobreak >nul
rem curl 은 한글 인자를 옛 코드(CP949)로 받아 깨뜨린다 - 출력 형식은 영어로 둔다.
curl.exe -s -o nul -w "  HTTP %%{http_code}\n" --max-time 90 "%SITE_URL%"
curl.exe -s --max-time 30 "%SITE_URL%api/about"
echo.
timeout /t 10 /nobreak >nul
for /f "delims=" %%f in ('dir /b /o-n "%SITE_DIR%\App_Data\logs\portal-*.log" 2^>nul') do if not defined LOG set "LOG=%SITE_DIR%\App_Data\logs\%%f"
if defined LOG (
    echo.
    echo   최근 로그:
    powershell -NoProfile -Command "Select-String -Path '%LOG%' -Encoding UTF8 -Pattern '\[about\]','\[storage\]','MQTT','error','fail' | Select-Object -Last 5 | ForEach-Object Line"
)
echo.
echo 완료. HTTP 200 과 새 버전(commit)이 보이면 정상입니다.
echo 문제가 있으면 되돌리기: %BK%\배포하기.cmd

:end
echo.
pause
