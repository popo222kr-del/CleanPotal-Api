@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
rem ============================================================================
rem  CleanPotal 운영 배포 - 운영 서버에서 publish 폴더 안의 이 파일을 더블클릭한다.
rem
rem  1) 개발 PC 의 publish 폴더를 원격 데스크톱으로 운영 서버 아무 곳(예: 바탕화면)에 통째로 복사
rem  2) 복사한 폴더 안의 "배포하기.cmd" 더블클릭 -> 관리자 권한 "예"
rem
rem  하는 일: 지금 버전 백업(실패하면 중단) -> 점검 안내 화면 켜기 -> 앱 풀 중지 -> 보존 파일을 뺀 나머지를
rem           이 폴더 내용으로 교체 -> 앱 풀 시작 -> 새 커밋으로 뜨는지 확인(안 뜨면 되돌릴지 묻는다)
rem           -> 성공하면 오래된 백업 정리(최근 5개)
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
set "NEWC="
if exist "%SRC%\build-info.json" for /f "delims=" %%c in ('powershell -NoProfile -Command "(Get-Content -Raw -Encoding UTF8 '%SRC%\build-info.json' | ConvertFrom-Json).commit"') do set "NEWC=%%c"

echo.
rem 백업을 먼저 - 사이트를 멈추기 전에 한다. 실패하면 아무것도 바꾸지 않고 끝낸다.
echo [1/6] 지금 버전 백업 -^> %BK%
robocopy "%SITE_DIR%" "%BK%" /E /XD App_Data /XF %EXCL_F% /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo.
    echo [중단] 백업에 실패했습니다^(디스크 공간 확인^). 운영은 그대로입니다.
    goto :end
)
if not exist "%BK%\CleanPotal.Api.dll" (
    echo.
    echo [중단] 백업 폴더에 CleanPotal.Api.dll 이 없습니다. 운영은 그대로입니다.
    goto :end
)
copy /y "%~f0" "%BK%\배포하기.cmd" >nul

echo [2/6] 점검 안내 화면 켜기, 앱 풀 중지^(%POOL%^)
> "%SITE_DIR%\app_offline.htm" echo ^<meta charset="utf-8"^>^<title^>점검 중^</title^>^<body style="font-family:sans-serif;text-align:center;padding-top:80px"^>^<h2^>업데이트 중입니다^</h2^>^<p^>1~2분 뒤 새로고침해 주세요.^</p^>^</body^>
timeout /t 3 /nobreak >nul
"%APPCMD%" stop apppool /apppool.name:%POOL% >nul 2>&1
timeout /t 5 /nobreak >nul
for /f "delims=" %%s in ('"%APPCMD%" list apppool /apppool.name:%POOL% /text:state 2^>nul') do set "PSTATE=%%s"
if /i not "%PSTATE%"=="Stopped" (
    echo   앱 풀 상태: %PSTATE% - 10초 더 기다립니다.
    timeout /t 10 /nobreak >nul
)

echo [3/6] 교체^(보존 파일은 그대로^)
robocopy "%SRC%" "%SITE_DIR%" /MIR /XD App_Data /XF %EXCL_F% /R:3 /W:3 /NFL /NDL /NJH /NP
if errorlevel 8 (
    echo.
    echo [오류] 복사 중 실패했습니다. 이전 버전으로 되돌립니다.
    goto :rollback
)

echo [4/6] 앱 풀 시작, 점검 안내 끄기
"%APPCMD%" start apppool /apppool.name:%POOL% >nul 2>&1
del /q "%SITE_DIR%\app_offline.htm" >nul 2>&1

echo [5/6] 새 버전이 떴는지 확인^(최대 3분^) 새 커밋: %NEWC%
powershell -NoProfile -Command "$u='%SITE_URL%api/about'; $want='%NEWC%'; for ($i = 0; $i -lt 18; $i++) { try { $a = Invoke-RestMethod -UseBasicParsing -Uri $u -TimeoutSec 20; if (-not $want -or $a.commit -eq $want) { '  정상: HTTP 200, commit ' + $a.commit; exit 0 } else { '  아직 예전 버전: ' + $a.commit } } catch { '  응답 없음 - 기다리는 중' }; Start-Sleep -Seconds 10 }; exit 1"
rem 괄호 블록 안에서 set /p 한 값은 같은 블록에서 읽히지 않는다 - goto 로 풀어 쓴다.
if not errorlevel 1 goto :healthy
echo.
echo [오류] 3분 안에 새 버전이 정상으로 뜨지 않았습니다. 아래 로그를 확인하세요.
call :showlog
echo.
set /p RB="이전 버전으로 되돌릴까요? (Y/N) "
if /i "%RB%"=="Y" goto :rollback
echo 되돌리지 않았습니다. 나중에 되돌리려면 %BK%\배포하기.cmd
goto :end

:healthy
echo [6/6] 오래된 배포 백업 정리^(최근 5개만^)
rem 성공한 뒤에만 정리한다. 지금 실행 중인 폴더^(백업에서 되돌리는 중일 수 있다^)는 지우지 않는다.
for /f "skip=5 delims=" %%d in ('dir /b /ad /o-n "%BACKUP_DIR%" 2^>nul') do if /i not "%BACKUP_DIR%\%%d"=="%SRC%" rd /s /q "%BACKUP_DIR%\%%d"
call :showlog
echo.
echo 완료. 문제가 보이면 되돌리기: %BK%\배포하기.cmd
goto :end

:rollback
echo.
echo [되돌리기] %BK% -^> %SITE_DIR%
> "%SITE_DIR%\app_offline.htm" echo ^<meta charset="utf-8"^>^<body style="font-family:sans-serif;text-align:center;padding-top:80px"^>^<h2^>업데이트 중입니다^</h2^>^</body^>
"%APPCMD%" stop apppool /apppool.name:%POOL% >nul 2>&1
timeout /t 5 /nobreak >nul
robocopy "%BK%" "%SITE_DIR%" /MIR /XD App_Data /XF %EXCL_F% /R:3 /W:3 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 echo [오류] 되돌리기 복사도 실패했습니다. %BK% 를 %SITE_DIR% 로 직접 복사하세요^(appsettings.local.json, App_Data 는 그대로 둔다^).
"%APPCMD%" start apppool /apppool.name:%POOL% >nul 2>&1
del /q "%SITE_DIR%\app_offline.htm" >nul 2>&1
timeout /t 5 /nobreak >nul
curl.exe -s -o nul -w "  HTTP %%{http_code}\n" --max-time 90 "%SITE_URL%"
curl.exe -s --max-time 30 "%SITE_URL%api/about"
echo.
echo 이전 버전으로 되돌렸습니다.
goto :end

:showlog
set "LOG="
for /f "delims=" %%f in ('dir /b /o-n "%SITE_DIR%\App_Data\logs\portal-*.log" 2^>nul') do if not defined LOG set "LOG=%SITE_DIR%\App_Data\logs\%%f"
if defined LOG (
    echo.
    echo   최근 로그:
    powershell -NoProfile -Command "Select-String -Path '%LOG%' -Encoding UTF8 -Pattern '\[about\]','\[storage\]','MQTT','error','fail' | Select-Object -Last 5 | ForEach-Object Line"
)
exit /b 0

:end
echo.
pause
