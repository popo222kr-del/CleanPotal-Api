# CleanPotal API

제조/세정 공정 현장체크 및 사내 포털 시스템의 **ASP.NET Core Web API** 백엔드.
기존 WPF 데스크톱 앱(CleanPotal)의 비즈니스 로직을 API-First 구조로 이식하여,
웹(React/Vue)과 모바일(MAUI 등)이 동일한 API를 호출하도록 한다.

## 아키텍처 (클린 아키텍처 3계층)

```
src/
├── CleanPotal.Api/              # 진입점 — Controllers, Program.cs, DI/JWT/CORS
│   └── Controllers/             # Auth, Users, Schedule, Handover, Portal
├── CleanPotal.Core/             # 도메인 — 의존성 없음
│   ├── Entities/                # User, ShiftSchedule, TeamEvent, Handover, Portal*
│   ├── DTOs/ · Interfaces/ · Security/
└── CleanPotal.Infrastructure/   # EF Core DbContext, 서비스 구현, 마이그레이션
    ├── Data/                    # CleanPotalDbContext, DbSeeder, Migrations
    └── Services/                # Auth/User/Schedule/Handover/Portal Service
```

의존 방향: `Api → Infrastructure → Core` (Core는 어디에도 의존하지 않음)

## 기술 스택

- ASP.NET Core 8 Web API (Controllers)
- EF Core 8 + SQLite (운영 시 PostgreSQL 교체 가능)
- JWT Bearer 인증 + 정책 기반 인가 (admin / CanManageFiles / CanManageSchedule)
- Swagger (개발용 API 탐색/테스트)

## 로컬 실행 (개발)

프론트 빌드 산출물(`wwwroot`)은 git에 올라가지 않으므로, `git pull` 직후
`dotnet run`만 하면 **API만 뜨고 화면은 비어 있다**. 창 2개로 띄운다.

```bash
# 창 1 — 백엔드 API (http://localhost:5001, Swagger: /swagger)
cd src/CleanPotal.Api
dotnet run

# 창 2 — 프론트 개발 서버 (http://localhost:5173 로 접속)
cd client
npm run dev
```

> 저장소 루트의 `start-dev.bat` 을 실행하면 위 두 개를 한 번에 띄운다.
> 배포와 동일하게 한 포트(5001)로 확인하려면 `cd client && npm run build` 후 `dotnet run`.

## 테스트

```bat
:: 저장소 루트에서
dotnet test
```

`tests/CleanPotal.Tests` — 비밀번호 해시(WPF 구형 해시 호환), 로그인(퇴사자 차단·
비밀번호 변경 시 토큰 무효화), 로그인 시도 제한, 근무표 도장·조회 입력 검증에 대한
회귀 테스트. DB가 필요한 테스트는 SQLite 인메모리를 쓰므로 실제 DB에 접속하지 않는다.

푸시·PR 마다 GitHub Actions(`.github/workflows/ci.yml`)가 백엔드 빌드·테스트와
프런트 타입검사·빌드·린트를 자동으로 돌린다.

## 배포 (IIS)

배포 단위는 **두 개**다. 포털(CleanPotal.Api)과 MES(ProductionManagement.Web)는 각자 프로세스로 뜨고,
브라우저에는 포털 주소 하나만 보인다 — 포털이 `/mes-runtime` 요청을 MES로 전달한다(`Mes:RuntimeUrl`).

```bat
:: 1) 프런트 빌드 → src/CleanPotal.Api/wwwroot 로 나온다
cd client && npm run build && cd ..

:: 2) 포털 게시
dotnet publish src\CleanPotal.Api -c Release -o publish

:: 3) MES 게시 (별도 폴더)
dotnet publish mes\src\ProductionManagement.Web -c Release -o publish-mes
```

MES는 **`localhost:5206`에만 바인딩**해서 띄운다(윈도우 서비스 또는 IIS의 별도 사이트).
사내망에 직접 열지 않는 것이 요점이다 — 브라우저는 포털을 통해서만 MES에 닿는다.

```bat
:: 예: MES 를 로컬 전용으로 기동
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://localhost:5206
publish-mes\ProductionManagement.Web.exe
```

MES 운영 데이터(`App_Data\Production.db`, `App_Data\Documents\`)는 게시물에 포함되지 않는다.
경로는 `publish-mes\appsettings.Production.json` 의 `MesData:RootPath` 로 지정한다(공유폴더 UNC 가능).

게시 폴더를 서버로 옮길 때 **사이트를 먼저 멈춰야 한다.**

```
1. 배포 폴더에 app_offline.htm 파일을 만든다 (내용은 아무거나) → 앱이 종료되고 DLL 잠금이 풀린다
2. 파일 전체를 덮어쓴다 (appsettings.local.json 은 덮어쓰지 말 것)
3. app_offline.htm 을 지운다
```

> **왜 중요한가**: IIS 는 실행 중인 `CleanPotal.Api.dll` 을 잠근다. 사이트를 멈추지 않고
> 복사하면 **wwwroot(화면)만 바뀌고 DLL(서버 프로그램)은 옛 버전 그대로 남는다.**
> 이때 새 화면이 새 API 를 부르면 **405 / 404** 가 난다.
> 증상: 화면에는 새 버튼이 보이는데 누르면 "요청 실패 (405)".

배포 후 확인: 서버의 `CleanPotal.Api.dll` 수정 시각이 방금인지 본다.

## 설정 (비밀값)

비밀값은 저장소에 두지 않는다. `src/CleanPotal.Api/appsettings.local.json.example` 을
같은 폴더에 **`appsettings.local.json`** 으로 복사해 채운다(= .gitignore 대상).
**배포 서버의 publish 폴더에도 같은 파일을 두어야 한다.**

| 설정 키 | 환경변수 | 설명 |
|---|---|---|
| `Jwt:Key` | `Jwt__Key` | 로그인 토큰 서명 키. **32바이트 이상**. 운영환경에서 없거나 짧으면 **서버가 시작되지 않는다**. |
| `Database:Provider` | `Database__Provider` | `SqlServer` 또는 `Sqlite`(기본) |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | SQL Server 연결 문자열 |

- 개발환경(`ASPNETCORE_ENVIRONMENT=Development`)에서는 `Jwt:Key` 가 없으면 개발 전용 임시 키로 동작한다.
- **키를 변경하면 기존 로그인 토큰이 모두 무효**가 되어 전원 재로그인이 필요하다.

## 최초 관리자 생성 (운영환경)

운영환경에서는 기본 비밀번호 계정(1004/1234)을 **자동 생성하지 않는다.**
계정이 하나도 없는 상태라면 아래로 최초 관리자를 만든다.

```bash
cd src/CleanPotal.Api
dotnet run -- create-admin <아이디>
# 비밀번호는 화면에 표시되지 않게 입력받는다(8자 이상).
```
> 개발환경에서는 계정이 하나도 없을 때만 `1004 / 1234` 가 자동 생성된다.

## 점검용 명령

```bash
# 로그인 문제 진단 (읽기 전용, DB를 변경하지 않음)
dotnet run -- check-login <아이디> ["비밀번호"]
#  → 계정 존재 여부, 저장된 비밀번호 "형식", 검증 성공 여부만 출력(값은 출력 안 함)
```

## 과거 자료 작성자 보정 (1회)

공지·인수인계·생산요청·회의록의 작성자를 **이름 → 계정 ID** 로 채운다.
삭제 권한을 이름이 아니라 계정 기준으로 판정하기 위한 준비 작업이다.

```bash
dotnet run -- backfill-authors
```

- `CreatorUserId` 가 비어 있는 행만 채우고, **이름이 정확히 한 사람과 일치할 때만** 채운다.
- 동명이인은 건너뛰고 목록으로 출력한다(사람이 판단할 몫).
- 여러 번 실행해도 안전하다. 실행 전까지는 예전처럼 이름으로 대조하므로 업무는 그대로 동작한다.

> 서버를 시작하면 모델에 새로 생긴 컬럼·테이블이 자동으로 **추가**된다(`SchemaUpgrader`).
> 없는 컬럼 `ADD` 와 없는 테이블 `CREATE` 만 수행하며, 기존 값·컬럼은 건드리지 않는다.

## 마이그레이션

```bash
dotnet ef migrations add <이름> \
  --project src/CleanPotal.Infrastructure \
  --startup-project src/CleanPotal.Api \
  --output-dir Data/Migrations
```
앱 시작 시 `Database.Migrate()`로 자동 적용된다.

## 이식 완료 도메인

| 도메인 | 엔드포인트 |
|---|---|
| 인증 (JWT) | `POST /api/auth/login` |
| 사용자 관리 | `/api/users` (admin) |
| 근무 일정 (도장 근무표) | `/api/schedule/roster`, `/api/schedule/stamp` |
| 팀 일정 | `/api/schedule/events` |
| 인수인계 | `/api/handover` |
| 업무 파일 통합(포탈) | `/api/portal` |
| 자재물류 일정 현황 | `/api/material`, `/api/material/roster`, `/api/material/destinations` |
| 생산관리 MES | `/mes-runtime/**` → 별도 프로세스(Blazor Server)로 전달. 자세한 내용은 [mes/README.md](mes/README.md) |
