# CleanPotal AI 공통 작업 환경

## 목적

Codex와 Claude가 서로 다른 복사본과 publish 폴더에서 작업해 코드·배포본이 엇갈리는 문제를 막는다. 이 문서는 작업 경로, Git 흐름, 빌드 및 배포 기준의 단일 기준이다.

## 정식 경로

| 구분 | 기준 |
|---|---|
| 정식 소스 저장소 | `C:\Users\owner\cleanpotal-api` |
| GitHub | `https://github.com/popo222kr-del/cleanpotal-api.git` |
| 표준 로컬 publish 출력 | `C:\Users\owner\cleanpotal-api\publish` (2026-09-24부터: 예전 `artifacts\publish`에서 경로만 짧게 변경) |
| 운영 IIS 배포 경로 | `C:\Webjueon\publish` |
| 운영 사이트 | `Cleanjueon`, `10.10.10.119:8713` |
| 테스트·Zigbee 게이트웨이 | `10.10.10.13` |

다음 경로는 통합 복구용 임시 참조이며 새 기능을 계속 개발하는 정식 폴더가 아니다.

```text
C:\Users\owner\Documents\Codex\2026-09-03\github-plugin-github-openai-curated-remote-3\cleanpotal-api-integration-20260921
```

검증된 통합 커밋은 다음과 같다.

```text
a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4
운영 DB 복구와 최신 Claude 변경 통합
```

이 커밋이 정식 저장소와 GitHub에 안전하게 반영된 것을 확인하기 전까지 임시 참조 폴더를 삭제하지 않는다.

## 작업 시작 절차

```powershell
Set-Location 'C:\Users\owner\cleanpotal-api'
git status --short --branch
git log -5 --oneline --decorate
```

- worktree가 dirty면 변경 출처와 목적을 먼저 확인한다.
- dirty 상태에서 `git pull`, `git reset --hard`, `git checkout -- <file>`을 실행하지 않는다.
- 다른 AI가 만든 변경도 사용자 작업으로 간주하여 임의로 폐기하지 않는다.
- 코드 수정은 항상 정식 소스 저장소에서만 한다.

## 현재 Git 정리 원칙

2026-09-24 기준 정식 저장소는 `cc1dd8b`에 있고 원격보다 2커밋 뒤이며, 추적·미추적 변경이 함께 있다. 별도 통합 저장소에는 원격의 `0b17460`, `6ce0277`과 로컬 복구 변경을 합친 깨끗한 `a9cc783`이 있다.

정리 담당자는 다음 순서를 따른다.

1. 정식 저장소의 dirty 변경 목록과 미추적 소스 파일을 보존한다.
2. 별도 통합 저장소의 `a9cc783`을 정식 저장소로 fetch하여 비교한다.
3. 현재 dirty 변경이 통합 커밋에 모두 포함됐는지 파일별로 검증한다.
4. 검증 전에는 기존 worktree를 강제로 전환하거나 임시 폴더를 삭제하지 않는다.
5. 검증 후 하나의 통합 브랜치를 GitHub에 올리고, 이후 Claude와 Codex가 그 브랜치에서만 이어서 작업한다.

## 빌드 기준

실제 코드 기준 API 대상 프레임워크는 현재 `net10.0`이다. 과거 문서의 ASP.NET Core 8 표기는 최신 코드와 다르므로 코드와 프로젝트 파일을 우선한다.

```powershell
Set-Location 'C:\Users\owner\cleanpotal-api'
dotnet test .\CleanPotal.sln
dotnet publish .\src\CleanPotal.Api\CleanPotal.Api.csproj `
    -c Release `
    -o .\publish
```

프론트엔드 빌드가 별도로 필요한 변경은 저장소 README와 package scripts를 확인해 먼저 빌드한다. publish를 만들 때 다음 정보를 함께 기록한다.

```powershell
git rev-parse HEAD
Get-FileHash '.\publish\CleanPotal.Api.dll' -Algorithm SHA256
```

`publish_new`, `deploy_ready_final`, `deploy_ready_final_v2`처럼 이름을 늘려 새 기준으로 삼지 않는다. 표준 출력은 `C:\Users\owner\cleanpotal-api\publish` 하나만 사용한다. 예전에 쓰던 `artifacts\publish`는 더 이상 갱신하지 않으며, 헷갈림 방지를 위해 지우거나 이름을 바꿔 둔다.

## 설정과 비밀값

- `appsettings.local.json`은 Git에 올리지 않는다.
- JWT 키, SQL 비밀번호, MQTT 비밀번호를 대화·로그·문서에 기록하지 않는다.
- 운영 SQL Server 설정은 운영 서버의 `C:\Webjueon\publish\appsettings.local.json`에서 관리한다.
- 운영 연결 공급자는 `Database:Provider = SqlServer`이다.
- SQL Server 주소는 `10.10.40.61`, 데이터베이스는 `JUEON`이며 인증정보는 로컬 설정에서만 확인한다.

## 테스트 서버 (배포 전 확인)

운영에 올리기 전에 이 개발 PC(10.10.10.13)에서 **같은 publish 결과물**을 먼저 띄워 확인한다.

```powershell
Set-Location 'C:\Users\owner\cleanpotal-api'
git pull origin claude/review-and-work-7yqzee
powershell -ExecutionPolicy Bypass -File .\tools\deploy-test.ps1
```

- 스크립트가 화면 빌드 → 전체 테스트 → `.\publish` → `C:\cleanpotal-test` 교체 → 새 창에서 실행(`http://10.10.10.13:8714`)까지 한다. 테스트가 하나라도 실패하면 멈춘다.
- 테스트 서버는 현재 **운영과 같은 DB** 를 쓴다(처음 한 번 개발 PC 의 `appsettings.local.json` 을 `C:\cleanpotal-test` 로 복사). 그래서 온·습도 구독·주기 기록은 끄고(운영 구독이 끊기거나 기록이 두 번 쌓이지 않게), 체크시트 QR 주소는 테스트 서버 주소를 쓴다. 테스트에서 넣은 자료는 운영 DB 에 남고, 테스트에서 올린 사진 파일은 이 PC 에만 있다.
- 확인이 끝나면 **같은 `.\publish` 폴더**를 운영에 배포한다(아래 배포 기준). 다시 빌드하지 않는다 — 확인한 것과 다른 결과물이 올라갈 수 있다.
- 나중에 운영 자료를 본격적으로 쓰기 시작하면 테스트 DB 를 따로 만들고 `C:\cleanpotal-test\appsettings.local.json` 의 연결 문자열만 바꾼다.

## 배포 기준

1. 배포할 Git 커밋과 테스트 결과를 확인하고, 테스트 서버(위)에서 먼저 확인한다.
2. IIS 사이트 또는 앱 풀을 중지한다.
3. 프로그램 파일과 `wwwroot`를 배포한다.
4. 아래 항목은 반드시 보존한다.

```text
C:\Webjueon\publish\appsettings.local.json
C:\Webjueon\publish\App_Data
C:\Webjueon\publish\cleanpotal.db
```

5. DLL 해시를 로컬 artifact와 비교한다.
6. IIS를 시작하고 DB 공급자, 로그인, 주요 화면, MQTT 상태를 확인한다.

운영 서버에서 직접 소스 코드를 수정하지 않는다. 긴급 설정 변경은 백업 파일을 만든 뒤 수행하고 변경 내용을 이 문서 또는 인수인계 문서에 기록한다.

## 운영 로그

IIS 안에서는 콘솔이 없어 예전에는 기동·스키마 보강·MQTT·예외 로그가 모두 사라졌다. 이제 개발환경이 아니면 콘솔 출력을 날짜별 파일에도 남긴다.

- 위치: `C:\Webjueon\publish\App_Data\logs\portal-YYYYMMDD.log` (`App_Data`는 배포 때 보존하므로 로그도 남는다)
- 보관: 30일이 지난 파일은 앱이 시작할 때 지운다
- 설정(선택, `appsettings.local.json`): `Logging:File:Enabled`, `Logging:File:Path`, `Logging:File:RetentionDays`
- 서버가 뜨지 않으면(500.30) 가장 최근 파일의 마지막 줄부터 본다. 로그에 비밀값을 쓰지 않는다.
- PowerShell 로 볼 때는 `-Encoding UTF8` 을 붙인다: `Get-ChildItem 'C:\Webjueon\publish\App_Data\logs' | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content -Tail 50 -Encoding UTF8`

## 첨부 저장 위치

사진·파일 첨부는 `Storage:AttachmentsPath`(NAS 공유폴더 가능, `Storage:ShareUser`/`SharePassword` 로 접속) 아래 `분류\yyyy-MM\날짜_시각_이름` 으로 저장한다. 설정·이전 순서·`migrate-attachments` 명령은 `docs/attachments-storage.md`.

## 운영 IIS 필수 설정

온·습도 수집(MQTT 구독)과 주기 기록은 IIS 앱 안의 백그라운드 서비스로 돈다. IIS 기본값(유휴 20분 종료, 첫 요청 때 시작)이면 밤·주말처럼 아무도 접속하지 않을 때 앱이 내려가 수집이 멈추고 그래프에 빈 구간이 생긴다. 아래 설정은 `applicationHost.config`에 저장되므로 배포로 `web.config`를 덮어써도 사라지지 않는다.

| 항목 | 값 | 이유 |
|---|---|---|
| 앱 풀 `startMode` | `AlwaysRunning` | 재부팅·재활용 뒤 요청을 기다리지 않고 바로 뜬다 |
| 앱 풀 `processModel.idleTimeout` | `00:00:00` | 접속이 없어도 내리지 않는다 |
| 앱 풀 `recycling.periodicRestart.time` | `00:00:00` + 매일 04:00 예약 | 29시간마다 아무 때나 재활용되지 않게 한다 |
| 앱 풀 `recycling.disallowOverlappingRotation` | `true` | 재활용 때 두 프로세스가 같은 MQTT ClientId로 붙어 서로 끊는 것을 막는다 |
| 사이트 `applicationDefaults.preloadEnabled` | `true` | 앱 풀이 뜨자마자 앱을 깨워 백그라운드 서비스를 시작한다 |
| 사이트 `requestLimits.maxAllowedContentLength` | `629145600`(600MB) | IIS 기본 30MB 때문에 여러 첨부를 한 번에 올리면 404.13으로 거절된다. 앱 한도와 같게 둔다 |
| Windows 기능 | Application Initialization | `preloadEnabled`가 동작하려면 필요하다 |

2026-09-24 기준으로 정했다. 서버를 새로 꾸리거나 사이트·앱 풀을 다시 만들면 이 설정을 다시 확인한다.

