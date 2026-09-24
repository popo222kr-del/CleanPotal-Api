# CleanPotal AI 공통 작업 환경

## 목적

Codex와 Claude가 서로 다른 복사본과 publish 폴더에서 작업해 코드·배포본이 엇갈리는 문제를 막는다. 이 문서는 작업 경로, Git 흐름, 빌드 및 배포 기준의 단일 기준이다.

## 정식 경로

| 구분 | 기준 |
|---|---|
| 정식 소스 저장소 | `C:\Users\owner\cleanpotal-api` |
| GitHub | `https://github.com/popo222kr-del/cleanpotal-api.git` |
| 표준 로컬 publish 출력 | `C:\Users\owner\cleanpotal-api\artifacts\publish` |
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
    -o .\artifacts\publish
```

프론트엔드 빌드가 별도로 필요한 변경은 저장소 README와 package scripts를 확인해 먼저 빌드한다. publish를 만들 때 다음 정보를 함께 기록한다.

```powershell
git rev-parse HEAD
Get-FileHash '.\artifacts\publish\CleanPotal.Api.dll' -Algorithm SHA256
```

`publish`, `publish_new`, `deploy_ready_final`, `deploy_ready_final_v2`처럼 이름을 늘려 새 기준으로 삼지 않는다. 표준 출력은 `artifacts\publish` 하나만 사용한다.

## 설정과 비밀값

- `appsettings.local.json`은 Git에 올리지 않는다.
- JWT 키, SQL 비밀번호, MQTT 비밀번호를 대화·로그·문서에 기록하지 않는다.
- 운영 SQL Server 설정은 운영 서버의 `C:\Webjueon\publish\appsettings.local.json`에서 관리한다.
- 운영 연결 공급자는 `Database:Provider = SqlServer`이다.
- SQL Server 주소는 `10.10.40.61`, 데이터베이스는 `JUEON`이며 인증정보는 로컬 설정에서만 확인한다.

## 배포 기준

1. 배포할 Git 커밋과 테스트 결과를 확인한다.
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

