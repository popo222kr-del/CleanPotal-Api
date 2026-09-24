param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'publish'),
    [string]$ApiBaseUrl = 'http://localhost:5001'
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'CleanPotal\FileLauncher'
$executable = Join-Path $installDirectory 'CleanPotal.FileLauncher.exe'
$protocolRoot = 'HKCU:\Software\Classes\cleanpotal'

if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory 'CleanPotal.FileLauncher.exe'))) {
    throw "게시된 실행 파일을 찾지 못했습니다: $PublishDirectory"
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PublishDirectory 'CleanPotal.FileLauncher.exe') -Destination $executable -Force

$settings = [ordered]@{
    ApiBaseUrl = $ApiBaseUrl
    AllowedRoots = @('\\10.10.40.98\Diff 세정팀\부서 공유 폴더')
    AllowInsecureLocalhost = $ApiBaseUrl -match '^http://(localhost|127\.0\.0\.1)(:\d+)?$'
}
$settings | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $installDirectory 'settings.json') -Encoding utf8

New-Item -Path $protocolRoot -Force | Out-Null
Set-Item -LiteralPath $protocolRoot -Value 'URL:CleanPotal File Launcher'
New-ItemProperty -Path $protocolRoot -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
New-Item -Path "$protocolRoot\DefaultIcon" -Force | Out-Null
Set-Item -LiteralPath "$protocolRoot\DefaultIcon" -Value ('"{0}",0' -f $executable)
New-Item -Path "$protocolRoot\shell\open\command" -Force | Out-Null
Set-Item -LiteralPath "$protocolRoot\shell\open\command" -Value ('"{0}" "%1"' -f $executable)

Write-Host "CleanPotal 실행 도우미 설치 완료: $executable"
Write-Host '등록 프로토콜: cleanpotal://'
