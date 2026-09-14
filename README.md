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
