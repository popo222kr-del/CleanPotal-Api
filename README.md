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

## 실행

```bash
cd src/CleanPotal.Api
dotnet run --urls "http://0.0.0.0:5001"
# http://localhost:5001 → Swagger UI
```

기본 관리자 계정: **1004 / 1234**

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
