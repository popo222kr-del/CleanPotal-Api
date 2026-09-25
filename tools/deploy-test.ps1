<#
  테스트 서버 배포 — 운영에 올리기 전에 이 PC 에서 같은 publish 결과물을 먼저 띄워 확인한다.

  하는 일:
    1) 작업 폴더 상태 확인(저장 안 한 변경이 있으면 멈추고 묻는다)
    2) 화면 빌드(npm run build) → 3) 전체 테스트(dotnet test) → 4) publish(.\publish)
    5) 돌고 있던 테스트 서버를 끄고 C:\cleanpotal-test 를 새 결과물로 바꾼다(설정·App_Data 는 그대로)
    6) 새 창에서 테스트 서버를 띄운다: http://<이 PC IP>:8714

  테스트 서버는 운영과 같은 DB 를 쓴다(appsettings.local.json 을 처음 한 번 복사). 그래서:
    - 온·습도 구독·주기 기록을 끈다(Zigbee__Mqtt__Enabled=false). 켜면 운영 구독이 끊기고 기록이 두 번 쌓인다.
    - 체크시트 QR 주소는 테스트 서버 주소를 쓴다(Checklist__QrBaseUrl).
    - 테스트에서 넣은 자료는 운영 DB 에 그대로 남는다. 테스트에서 올린 사진 파일은 이 PC 에만 있다.

  실행(관리자 PowerShell, 저장소 폴더에서):
    powershell -ExecutionPolicy Bypass -File .\tools\deploy-test.ps1
  테스트를 건너뛰려면 -SkipTests, 포트를 바꾸려면 -Port 8715
#>
param(
    [switch]$SkipTests,
    [int]$Port = 8714,
    [string]$TestDir = 'C:\cleanpotal-test',
    [string]$HostIp = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

function Step($n, $text) { Write-Host ""; Write-Host "[$n] $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host ""; Write-Host "중단: $text" -ForegroundColor Red; exit 1 }

# ── 1. 작업 폴더 상태 ──
Step 1 '작업 폴더 상태 확인'
git status --short --branch
$dirty = git status --porcelain
if ($dirty) {
    Write-Host '위에 커밋하지 않은 변경이 있습니다. 이 변경까지 테스트 서버에 올라갑니다.' -ForegroundColor Yellow
    if ((Read-Host '계속할까요? (y/N)') -ne 'y') { Fail '사용자가 멈춤' }
}
Write-Host ("커밋: " + (git log -1 --oneline))

# ── 2. 화면 빌드 ──
Step 2 '화면 빌드(npm run build)'
Push-Location .\client
npm run build
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Fail '화면 빌드 실패' }

# ── 3. 테스트 ──
if ($SkipTests) {
    Step 3 '테스트 건너뜀(-SkipTests)'
} else {
    Step 3 '전체 테스트(dotnet test)'
    dotnet test .\CleanPotal.sln
    if ($LASTEXITCODE -ne 0) { Fail '테스트 실패 — 테스트 서버에도 올리지 않습니다' }
}

# ── 4. publish ──
Step 4 'publish(.\publish)'
Remove-Item .\publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish .\src\CleanPotal.Api\CleanPotal.Api.csproj -c Release -o .\publish
if ($LASTEXITCODE -ne 0) { Fail 'publish 실패' }
$js = (Select-String -Path .\publish\wwwroot\index.html -Pattern 'index-[^"]*\.js').Matches.Value | Select-Object -First 1
$hash = (Get-FileHash .\publish\CleanPotal.Api.dll -Algorithm SHA256).Hash

# ── 5. 테스트 서버 교체 ──
Step 5 "테스트 서버 교체($TestDir)"
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -match 'CleanPotal\.Api\.dll' -and $_.CommandLine -match ":$Port" } |
    ForEach-Object { Write-Host "  돌고 있던 테스트 서버(PID $($_.ProcessId))를 끕니다."; Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 2

New-Item $TestDir -ItemType Directory -Force | Out-Null
Get-ChildItem $TestDir -Exclude 'appsettings.local.json', 'App_Data' | Remove-Item -Recurse -Force
Copy-Item .\publish\* $TestDir -Recurse -Force

$testConfig = Join-Path $TestDir 'appsettings.local.json'
if (-not (Test-Path $testConfig)) {
    $devConfig = Join-Path $repo 'src\CleanPotal.Api\appsettings.local.json'
    if (-not (Test-Path $devConfig)) { Fail "설정 파일이 없습니다: $devConfig" }
    Copy-Item $devConfig $testConfig
    Write-Host '  개발 PC 설정(appsettings.local.json)을 테스트 서버로 처음 복사했습니다.'
}
# 운영 모드로 띄우므로 로그인 서명 키(Jwt:Key, 32바이트 이상)가 꼭 있어야 한다. 개발 PC 설정에는
# 보통 없어서(개발 모드는 임시 키로 돈다) 테스트 서버 설정에만 새로 만들어 넣는다. 키 값은 출력하지 않는다.
# 운영과 다른 키라서 운영에서 받은 로그인은 테스트 서버에서 쓰이지 않는다(테스트 서버에서 따로 로그인).
$cfg = Get-Content $testConfig -Raw | ConvertFrom-Json
$jwtKey = if ($cfg.PSObject.Properties['Jwt']) { $cfg.Jwt.Key } else { $null }
if (-not $jwtKey -or [Text.Encoding]::UTF8.GetByteCount([string]$jwtKey) -lt 32) {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $newKey = [Convert]::ToBase64String($bytes)
    if ($cfg.PSObject.Properties['Jwt']) { $cfg.Jwt | Add-Member -NotePropertyName Key -NotePropertyValue $newKey -Force }
    else { $cfg | Add-Member -NotePropertyName Jwt -NotePropertyValue ([pscustomobject]@{ Key = $newKey }) }
    $cfg | ConvertTo-Json -Depth 20 | Set-Content -Path $testConfig -Encoding UTF8
    Write-Host '  테스트 서버 설정에 로그인 서명 키(Jwt:Key)를 새로 만들어 넣었습니다.'
}
if (-not $cfg.PSObject.Properties['Database'] -or -not $cfg.Database.Provider) {
    Fail "테스트 서버 설정($testConfig)에 Database:Provider 가 없습니다. 운영 모드에서는 꼭 있어야 합니다."
}
# 어느 DB 를 보는지만 보여 준다(비밀번호는 출력하지 않는다).
try {
    $db = ($cfg.ConnectionStrings.Default -split ';' | Where-Object { $_ -match '^\s*(Server|Data Source|Database|Initial Catalog)\s*=' }) -join '; '
    Write-Host "  DB: $($cfg.Database.Provider) / $db"
} catch { Write-Host '  (설정 파일 DB 항목을 읽지 못했습니다)' -ForegroundColor Yellow }

# ── 6. 실행 ──
if (-not $HostIp) {
    $ips = Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        Sort-Object { if ($_.IPAddress -like '10.10.*') { 0 } else { 1 } }
    $HostIp = ($ips | Select-Object -First 1).IPAddress
}
$url = "http://${HostIp}:$Port"

# 휴대폰(QR)으로도 열어 보려면 방화벽에서 이 포트를 연다(처음 한 번).
$ruleName = "CleanPotal 테스트 서버 $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    try {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        Write-Host "  방화벽에 $Port 포트를 열었습니다."
    } catch { Write-Host "  방화벽 규칙을 만들지 못했습니다(관리자 권한 필요). 휴대폰 확인이 필요하면 관리자 PowerShell 로 다시 실행하세요." -ForegroundColor Yellow }
}

Step 6 "테스트 서버 시작 — 새 창에 로그가 나옵니다(창을 닫으면 테스트 서버가 꺼집니다)"
# 실행 명령은 파일로 만들어 넘긴다(여러 줄 명령을 인자로 넘기면 Windows PowerShell 에서 깨질 수 있다).
$runFile = Join-Path $TestDir 'run-test-server.ps1'
@"
`$Host.UI.RawUI.WindowTitle = 'CleanPotal 테스트 서버 :$Port'
`$env:ASPNETCORE_ENVIRONMENT = 'Production'
`$env:Zigbee__Mqtt__Enabled = 'false'
`$env:Checklist__QrBaseUrl = '$url'
Set-Location '$TestDir'
dotnet .\CleanPotal.Api.dll --urls http://0.0.0.0:$Port
"@ | Set-Content -Path $runFile -Encoding UTF8
Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', $runFile | Out-Null

$ok = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $ok = $true; break }
    } catch { }
}
Write-Host ""
if ($ok) {
    Write-Host "테스트 서버가 떴습니다: $url" -ForegroundColor Green
} else {
    Write-Host "2분 안에 응답이 없습니다. 테스트 서버 창의 로그와 $TestDir\App_Data\logs 를 확인하세요." -ForegroundColor Red
}
Write-Host "  화면 파일: $js"
Write-Host "  DLL SHA256: $hash"
Write-Host ''
Write-Host '확인이 끝나면 같은 .\publish 폴더를 운영(C:\Webjueon\publish)에 복사합니다(docs\AI_WORKSPACE_STANDARD.md 배포 기준).'
