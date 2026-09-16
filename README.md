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

- **.NET 10** — MES(ProductionManagement)와 런타임을 맞췄다.
  .NET 8 은 2026-11 지원이 끝나고, MES 를 포털로 흡수하려면 한 런타임이어야 한다.
  **서버에는 ASP.NET Core 10 Hosting Bundle 이 설치돼 있어야 한다**(없으면 사이트가 500 을 낸다).
- ASP.NET Core Web API (Controllers)
- EF Core + SQL Server (개발은 SQLite)
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

### 테스트 서버 자동 배포 (선택)

위 절차를 사람이 손으로 하지 않아도 되게, 푸시 → CI 통과 → 자동 배포까지 이어 붙일 수 있다.
`.github/workflows/ci.yml` 의 `deploy` 잡이 그 역할을 한다.

**대상은 테스트 서버뿐이다. 운영 서버는 이 방식으로 배포하지 않는다.**

GitHub 은 사내망에 접속할 수 없으므로, **테스트 서버 PC 에 러너를 설치해 당겨가게** 한다
(방화벽을 열 필요가 없다).

```powershell
# 1) 테스트 서버 PC 에서, 관리자 PowerShell
#    저장소 → Settings → Actions → Runners → New self-hosted runner (Windows)
#    거기 나오는 다운로드/설정 명령을 그대로 실행한다.
#    라벨을 물어보면 반드시 아래를 포함시킨다:
#        windows, cleanpotal-test

# 2) 서비스로 등록해 부팅 시 자동 실행
./svc.cmd install
./svc.cmd start
```

그다음 저장소 **Settings → Secrets and variables → Actions → Variables** 에 값을 넣는다.

| 변수 | 예 | 설명 |
|---|---|---|
| `DEPLOY_BRANCH` | `claude/review-and-work-7yqzee` | 이 브랜치가 푸시되면 배포한다 |
| `DEPLOY_PATH` | `C:\inetpub\wwwroot\cleanpotal` | 배포 폴더 |
| `DEPLOY_HEALTH_URL` | `http://localhost/` | (선택) 배포 후 응답 확인 |

`DEPLOY_BRANCH` 가 비어 있으면 배포 잡은 통째로 건너뛴다 — 변수를 채우기 전까지는
아무 일도 일어나지 않는다. 브랜치를 바꿀 때도 워크플로를 고칠 필요 없이 변수만 바꾸면 된다.

동작 순서는 위 수동 절차와 같다: 프런트 빌드 → `dotnet publish` → `app_offline.htm` 생성 →
파일 복사 → `app_offline.htm` 삭제 → DLL 수정 시각 확인.

- `appsettings.local.json`(비밀값)은 복사 대상에서 제외한다.
- `wwwroot` 만 미러 복사해 옛 화면 파일을 정리한다(전부 빌드 산출물이라 안전).
- 복사가 실패해도 `app_offline.htm` 은 반드시 지운다 — 사이트가 내려간 채로 남지 않는다.

> **알아둘 것**: 자체 호스팅 러너는 워크플로에 적힌 명령을 그 PC 에서 실행한다.
> 즉 저장소에 푸시할 수 있는 사람은 그 PC 에서 코드를 돌릴 수 있다.
> 비공개 저장소에 인원이 제한적일 때만 쓴다.

## MES 데이터베이스

MES(ProductionManagement)는 **포털과 같은 DB** 를 쓴다. LOT 과 사원·일정을 한 화면에서
엮으려면 DB 가 갈려 있으면 안 되기 때문이다.

- 테이블에는 **`Mes` 접두사**가 붙는다(`MesLots`, `MesProducts` …).
  접두사가 없으면 `Users`·`InspectionRecords` 가 포털 테이블과 그대로 부딪힌다.
- 연결 설정은 포털과 **같은 키**를 본다 — `ConnectionStrings:Default`, `Database:Provider`.
  아무것도 없으면 예전처럼 `App_Data/Production.db`(SQLite)로 떨어진다.
- 연결 문자열은 **포털이 정한 값을 MES 가 그대로 받는다**(`MesModule.AddMes`).
  설정이 비어 있을 때 포털이 만들어 쓰는 SQLite 파일 경로까지 함께 넘어가므로 둘이 갈리지 않는다.
- 아직 남아 있는 MES(Blazor) 프로세스는 자기 설정을 따로 읽는다. 비밀값을 두 파일에 복사하지
  않으려면 **환경변수**로 한 번만 주는 것이 좋다.

```bat
setx ConnectionStrings__Default "..."
setx Database__Provider "SqlServer"
setx MesData__RootPath "\\서버\공유폴더\MesData"
```

`MesData__RootPath` 는 성적서 같은 **첨부파일이 실제로 놓이는 폴더**다(그 아래 `Documents/`).
비워 두면 저장소 안 MES 앱이 쓰던 `mes/src/ProductionManagement.Web/App_Data` 를 그대로 이어 쓰고,
그것도 없으면 API 폴더 밑에 만든다. 서버에 올릴 때는 공유폴더로 직접 지정하는 것이 확실하다 —
배포할 때마다 폴더가 갈리면 예전 성적서가 안 보인다. 포털이 뜰 때 `[mes] 첨부파일 루트:` 로 찍는다.

스키마는 앱이 뜰 때 `MesSchemaInitializer` 가 **MES 테이블이 없을 때만** EF 모델대로 만든다.
`EnsureCreated` 는 "테이블이 하나도 없을 때만" 동작해서 포털 DB 에서는 아무 일도 하지 않고,
기존 SQLite 마이그레이션은 접두사가 붙기 전에 만들어져 더 이상 모델과 맞지 않기 때문이다.
추가만 하고 DROP·컬럼 변경은 하지 않는다.

## MES 를 포털 안으로 (진행 중)

MES 는 **별도 앱을 띄워 쓰는 것이 아니라** 포털 웹앱의 한 부분이 되는 것이 목표다.
화면만 React 로 새로 만들고, 업무 로직(LOT 채번·공정 이동 규칙·이력 조회)은 MES 것을 그대로 쓴다 —
같은 규칙을 두 번 구현하면 두 곳이 반드시 갈라지기 때문이다.

| 단계 | 내용 | 상태 |
|---|---|---|
| 1 | 포털을 .NET 10 으로 — 런타임 통일 | 완료 |
| 2 | MES 를 포털과 같은 DB 로(`Mes` 접두사) | 완료 |
| 3 | MES 업무 계층을 포털 API 로 노출(JWT·기존 권한) | 완료 |
| 4 | 화면 19개를 React 로 하나씩 이관 | 완료 |
| 5 | Blazor 프로젝트·`mes/` 폴더·YARP 프록시 제거 | 예정 |

포털 API 는 `mes/src/ProductionManagement.{Application,Infrastructure}` 를 직접 참조한다.
이 둘은 화면에 기대지 않는 순수 `net10.0` 라이브러리라 그대로 쓸 수 있다.
Blazor(`ProductionManagement.Web`)는 참조하지 않는다.

바꿔 끼우는 것은 "지금 누가 작업 중인가" 하나다. MES 는 한 사람이 쓰는 데스크톱 전제라 이 값을
**Singleton** 에 담아 두는데, 그대로 두면 서버에 사용자가 한 명만 존재하게 되어 누가 작업하든
이력의 작업자가 같은 사람으로 남는다. `PortalCurrentUserProvider` 가 이를 요청 단위(JWT)로 덮는다
(회귀 테스트: `MesModuleTests`).

MES 는 관리자 전용이 아니라 **전 직원이 권한을 받아 함께 쓰는** 기능이다. 전용 권한 영역
`mes` 를 갖고, 기본 등급은 1(조회)이라 이미 등록된 사용자도 서버가 한 번 뜨면 바로 볼 수 있다.
작업(전산등록·공정 이동)은 관리자가 등급 2 로 올려 준다.

등급과 **다른 축**으로 MES 세부 권한 여섯 가지(업체·제품·공정 마스터, 성적서, 공정 무효화,
MES 사용자 관리)가 있다. 마스터를 잘못 바꾸면 이후 모든 LOT 이 영향을 받아서, 편집 등급을 준
작업자라도 이것은 사람을 골라 켜 준다 — 사용자 계정 관리 → 권한 탭 → `MES (생산관리)` 아래
`세부 권한` 칩. 자세한 것은 `docs/permissions.md`.

**옮긴 화면**

| 화면 | 포털 경로 | API |
|---|---|---|
| Dash Board | `/mes` | `GET /api/mes/dashboard`, `/dashboard/lots` |
| LOT 현황 조회 | `/mes/history` | `GET /api/mes/lot/history?keyword=` |
| TAT 조회 | `/mes/tat` | `GET /api/mes/lot/tat?from=&to=` |
| LOT 스캔 | `/mes/scan` | `GET /api/mes/lot/scan?code=`, `POST /api/mes/lot/decode` |
| 전산등록 (CREATE) | `/mes/register` | `GET /api/mes/register/reference`, `POST /api/mes/register` |
| OPER 공정 (9개 코드 전부) | `/mes/oper/:code` | `GET/POST /api/mes/oper/…` |
| 출력 관리 (성적서 · 런시트) | OPER 화면 안의 창 | `GET/POST /api/mes/lot/{id}/documents`, `/runsheet` |
| HOLD 관리 | `/mes/holds` | `GET /api/mes/holds` |
| 재작업 관리 | `/mes/reworks` | `GET /api/mes/reworks` |
| 성적서 조회 | `/mes/certificates` | `GET /api/mes/certificates` |
| 이력 삭제 | `/mes/history-void` | `GET /api/mes/process-history`, `POST …/void` |
| Batch | `/mes/batch` | `GET/POST /api/mes/batch` |
| 입 · 출고 현황 조회 | `/mes/lot-inout` | `GET /api/mes/lot-inout`, `POST …/drill` |
| 세정 이력 조회 (+ 감사 로그) | `/mes/cleaning-history` | `GET /api/mes/cleaning-history`, `…/audit` |
| 셋업 — 업체 관리 탭 | `/mes/setup?tab=customer` | `GET/POST/PUT /api/mes/setup/customers` |
| 셋업 — 공정 관리 탭 | `/mes/setup?tab=process` | `GET/POST/PUT /api/mes/setup/processes`, `…/routes` |
| 셋업 — 제품 관리 탭 | `/mes/setup?tab=product` | `GET/POST/PUT /api/mes/setup/product`, `…/create-from` 외 |
| 셋업 — 단가/이미지 탭 | `/mes/setup?tab=price` | `GET /api/mes/setup/products/{id}/price-image` 외 |

화면 공용 스타일은 `client/src/pages/mes/Mes.css` 하나에 모으고, 상태 표기·날짜 형식 같은
공용 규칙은 `client/src/pages/mes/lot.ts` 에 둔다 — 화면이 19개라 각자 갖게 두면 금방 어긋난다.

**MES 화면 19개를 모두 옮겼다.** `/mes/*` 는 아직 남아 있어 옮기지 않은 주소(예전 링크 등)를
받아 기존 MES 를 iframe 으로 띄우지만, 사이드바에서 갈 수 있는 화면은 전부 포털 페이지다.
다음 단계(5)는 Blazor 프로젝트와 `mes/` 폴더, YARP 프록시를 걷어내는 일이다.

**Blazor(MES) 프로세스를 내려도 포털은 그대로 돈다.** 포털은 MES 의 업무 계층(라이브러리)만
참조하고 MES 앱에는 기대지 않는다 — 뜰 때도, 돌 때도 그 프로세스를 찾지 않는다. 내려 두면
옛 `/mes/*` 주소에서 안내 화면이 뜨는 것이 전부다. 옮긴 화면이 정말 혼자 서는지 확인하려면
MES 를 잠깐 내리고 사이드바를 한 바퀴 돌아 보면 된다.

### 이번 변경을 확인하는 순서

서버를 **다시 띄워야** 한다 — `Users.MesPermissions` 컬럼 추가와 MES 기준 데이터 확인이
시작할 때 일어난다.

1. 사용자 계정 관리 → 권한 탭에 `MES (생산관리)` 줄과 그 아래 `세부 권한` 칩 6개가 보이는가.
2. 관리자가 아닌 계정 하나에 MES 편집(2) + `제품 마스터` 만 켜 보고, 그 계정으로 셋업에 들어가
   **제품 셋업 · 단가/이미지** 두 탭만 보이는지(둘 다 제품 마스터 권한을 쓴다).
   업체 관리 · 공정 관리 탭은 보이지 않아야 한다.
3. 그 계정에서 셋업 > 제품을 저장해 본다(통과). `공정 무효화` 는 끄고 이력 삭제를 눌러 보면
   "MES 세부 권한 '공정 무효화' 이(가) 필요합니다" 가 떠야 한다.
4. 관리자로 돌아와 사용자 관리 → 변경 이력에 `MES 세부 권한 +제품 마스터` 가 남았는지.
5. `/mes/oper/3000` 에서 TRAN 목록이 비어 있지 않은지(기준 데이터가 깔렸다는 뜻이다).

> 성적서 엑셀에 값 채우기(특이사항 이미지 삽입 포함)는 Excel COM 이라 서버에서 돌지 않는다.
> 이 제약은 MES 를 웹으로 올린 시점부터 있던 것이고 이번에 새로 생긴 것이 아니다. 다만 MES 웹판은
> 아무것도 하지 않고 **"넣었습니다" 라고 답했다** — 포털은 못 했다고 답한다. 성적서 **받기·올리기**와
> 런시트 생성은 정상 동작한다. 데스크톱을 내리기 전에 정해야 하는 항목이라
> `docs/known-issues.md` 에 선택지를 적어 뒀다.

바코드·QR 을 다루는 `BarcodeService` 와 OPER 화면 목록 `OperScreens` 는 포털과 MES 화면이
같이 쓰므로 공용 계층으로 올렸다(`ProductionManagement.Infrastructure.Imaging`,
`ProductionManagement.Application.Screens`). 사진 해독을 서버가 하는 이유는 그대로다 —
브라우저 실시간 카메라는 HTTPS 에서만 되는데 사내 Wi-Fi 는 HTTP 로 붙는다.

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
