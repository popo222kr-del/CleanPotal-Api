<#
  CleanPotal 서버 관리 — 개발·테스트·운영 세 서버를 한 곳에서 켜고, 상태·버전을 비교하고, 브라우저로 연다.

  | 구분   | 주소                      | 무엇                                               |
  |--------|---------------------------|----------------------------------------------------|
  | 개발   | http://<이 PC>:5173       | 코드를 고치면 바로 반영(dotnet watch + Vite). 확인용 |
  | 테스트 | http://<이 PC>:8714       | 운영에 올릴 publish 결과물 그대로. 배포 전 검증     |
  | 운영   | http://10.10.10.119:8713  | 실제 사용(IIS)                                      |

  실행(저장소 폴더에서): powershell -ExecutionPolicy Bypass -File .\tools\servers.ps1
  메뉴 6번으로 바탕화면 바로가기를 만들면 다음부터는 더블클릭으로 연다.
  화면 왼쪽 위 배지(개발·테스트·운영)와 브라우저 탭 제목으로도 어느 서버인지 구분된다.
#>
param(
    [string]$ProdUrl = 'http://10.10.10.119:8713',
    [int]$TestPort = 8714,
    [int]$DevPort = 5173,
    [string]$TestDir = 'C:\cleanpotal-test'
)

$ErrorActionPreference = 'Continue'
# 한글이 깨지지 않게 — git·dotnet·npm 은 UTF-8 로 내보내는데 Windows PowerShell 5 는 기본(CP949)으로 읽는다.
# 이 창의 콘솔을 UTF-8 로 맞추면 커밋 제목·테스트 이름·로그의 한글이 그대로 보인다. 이 창에서 띄우는 프로그램에도 이어진다.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$Host.UI.RawUI.WindowTitle = 'CleanPotal 서버 관리'

$ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Sort-Object { if ($_.IPAddress -like '10.10.*') { 0 } else { 1 } } |
    Select-Object -First 1).IPAddress
if (-not $ip) { $ip = 'localhost' }

$servers = @(
    [pscustomobject]@{ Key = 'd'; Name = '개발';   Url = "http://${ip}:$DevPort";  Probe = "http://localhost:$DevPort" }
    [pscustomobject]@{ Key = 't'; Name = '테스트'; Url = "http://${ip}:$TestPort"; Probe = "http://localhost:$TestPort" }
    [pscustomobject]@{ Key = 'p'; Name = '운영';   Url = $ProdUrl;                 Probe = $ProdUrl }
)

function Probe($s) {
    # /api/about 이 있으면 버전까지, 없으면(이 기능 전 빌드) 켜져 있는지만 본다.
    try {
        $a = Invoke-RestMethod "$($s.Probe)/api/about" -TimeoutSec 4
        if ($a.env) { return [pscustomobject]@{ Up = $true; About = $a } }
    } catch { }
    try {
        $r = Invoke-WebRequest "$($s.Probe)/" -UseBasicParsing -TimeoutSec 4
        return [pscustomobject]@{ Up = ($r.StatusCode -eq 200); About = $null }
    } catch { return [pscustomobject]@{ Up = $false; About = $null } }
}

function Show-Status {
    Write-Host ''
    Write-Host '확인 중…' -ForegroundColor DarkGray
    $head = [string](git log -1 --format='%h %s' 2>$null)

    $rows = foreach ($s in $servers) {
        $p = Probe $s
        $build = if (-not $p.Up) { '' }
                 elseif (-not $p.About) { '(버전 정보 없음 — 이전 빌드)' }
                 elseif ($s.Key -eq 'd') { '(실시간 코드)' }
                 elseif ($p.About.commit) { "$($p.About.commit)$(if ($p.About.dirty) { '+수정' }) · $($p.About.builtAt)" }
                 else { '(빌드 정보 없음)' }
        [pscustomobject]@{
            구분 = $s.Name; 주소 = $s.Url
            상태 = if ($p.Up) { '켜짐' } else { '꺼짐' }
            빌드 = $build
            _commit = if ($p.About) { $p.About.commit } else { '' }
        }
    }
    $rows | Select-Object 구분, 주소, 상태, 빌드 | Format-Table -AutoSize | Out-Host
    Write-Host "지금 코드(git): $head"

    $t = $rows | Where-Object 구분 -eq '테스트'
    $p = $rows | Where-Object 구분 -eq '운영'
    if ($t._commit -and $p._commit) {
        if ($t._commit -eq $p._commit) { Write-Host '→ 테스트와 운영이 같은 빌드입니다.' -ForegroundColor Green }
        else { Write-Host '→ 테스트와 운영의 빌드가 다릅니다. 테스트에서 확인이 끝났으면 운영에 배포하세요.' -ForegroundColor Yellow }
    }
}

function Start-Dev {
    Write-Host '개발 모드 창들을 엽니다(start-dev.bat). 코드를 고치면 바로 반영됩니다.'
    Start-Process -FilePath (Join-Path $repo 'start-dev.bat') -WorkingDirectory $repo
    Write-Host "잠시 뒤 $($servers[0].Url) 로 여세요(메뉴 5 → d)."
}

function Deploy-Test {
    Write-Host '최신 코드를 받습니다(git pull).'
    git pull
    if ($LASTEXITCODE -ne 0) { Write-Host 'git pull 이 실패했습니다. 위 메시지를 확인하세요(Aborting 이면 멈추고 해결).' -ForegroundColor Red; return }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'deploy-test.ps1')
}

function Restart-Test {
    $run = Join-Path $TestDir 'run-test-server.ps1'
    if (-not (Test-Path $run)) { Write-Host '테스트 서버를 한 번도 배포하지 않았습니다. 메뉴 2 를 먼저 하세요.' -ForegroundColor Yellow; return }
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
        Where-Object { $_.CommandLine -match 'CleanPotal\.Api\.dll' -and $_.CommandLine -match ":$TestPort" } |
        ForEach-Object { Write-Host "  돌고 있던 테스트 서버(PID $($_.ProcessId))를 끕니다."; Stop-Process -Id $_.ProcessId -Force }
    Start-Process powershell -ArgumentList '-NoExit', '-ExecutionPolicy', 'Bypass', '-File', $run | Out-Null
    Write-Host "테스트 서버를 다시 켰습니다(빌드 없이). 20초쯤 뒤 $($servers[1].Url)"
}

function Open-Browser {
    $k = (Read-Host '어느 서버? d=개발  t=테스트  p=운영').Trim().ToLower()
    $s = $servers | Where-Object Key -eq $k
    if ($s) { Start-Process $s.Url } else { Write-Host '잘못 골랐습니다.' }
}

function New-Shortcut {
    $desk = [Environment]::GetFolderPath('Desktop')
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Join-Path $desk 'CleanPotal 서버 관리.lnk'))
    $lnk.TargetPath = 'powershell.exe'
    $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $lnk.WorkingDirectory = $repo
    $lnk.Save()
    Write-Host '바탕화면에 "CleanPotal 서버 관리" 바로가기를 만들었습니다.' -ForegroundColor Green
}

while ($true) {
    Write-Host ''
    Write-Host '==================== CleanPotal 서버 관리 ====================' -ForegroundColor Cyan
    foreach ($s in $servers) { Write-Host ("  {0,-4} {1}" -f $s.Name, $s.Url) }
    Write-Host '--------------------------------------------------------------'
    Write-Host '  1  상태·버전 보기 (세 서버 켜짐 여부, 테스트=운영 같은 빌드인지)'
    Write-Host '  2  테스트 서버 배포 (git pull → 빌드·테스트 → :8714)'
    Write-Host '  3  테스트 서버 다시 켜기 (빌드 없이 — 재부팅 뒤 등)'
    Write-Host '  4  개발 모드 켜기 (코드 고치면 바로 반영 :5173)'
    Write-Host '  5  브라우저로 열기'
    Write-Host '  6  바탕화면 바로가기 만들기'
    Write-Host '  0  끝'
    switch ((Read-Host '번호').Trim()) {
        '1' { Show-Status }
        '2' { Deploy-Test }
        '3' { Restart-Test }
        '4' { Start-Dev }
        '5' { Open-Browser }
        '6' { New-Shortcut }
        '0' { return }
        default { Write-Host '번호를 다시 고르세요.' }
    }
}
