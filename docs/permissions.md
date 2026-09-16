# 권한 정책 매트릭스

서버 권한은 **영역(area) × 등급(level)** 으로 검사한다. 등급은 사용자별로 DB에 저장되며
(`AccessSchedule`, `AccessRoster`, `AccessHandover`, `AccessField`, `AccessOffice`),
`DbPermissionHandler` 가 **요청 시마다 DB를 조회**하므로 권한을 바꾸면 재로그인 없이 즉시 반영된다.

| 등급 값 | 의미 |
|---|---|
| 0 | 없음 (접근 불가) |
| 1 | 조회 |
| 2 | 편집 (조회 포함) |

`IsAdmin` 정책은 등급과 무관하게 `User.IsAdmin = true` 인 계정만 통과한다.

## 정책 목록

| 정책 | 영역 | 필요 등급 |
|---|---|---|
| `ViewSchedule` / `EditSchedule` | schedule (일정관리) | 1 / 2 |
| `ViewRoster` / `EditRoster` | roster (근무표) | 1 / 2 |
| `ViewHandover` / `EditHandover` | handover (현장 인수인계) | 1 / 2 |
| `ViewField` / `EditField` | field (현장 점검) | 1 / 2 |
| `ViewOffice` / `EditOffice` | office (OFFICE 업무) | 1 / 2 |
| `ViewReports` / `EditReports` | reports (생산미팅 ∪ 주간보고) | 1 / 2 |
| `IsAdmin` | admin | 관리자만 |

## 컨트롤러별 적용 현황

| 컨트롤러 | 조회 | 등록·수정·삭제 | 비고 |
|---|---|---|---|
| Auth | 익명(login) | 본인 인증 필요(me, change-credentials) | 로그인은 익명이어야 정상 |
| Users | — | `IsAdmin` | 계정·권한 관리 |
| Schedule (일정/근무표) | `ViewSchedule` / `ViewRoster` | `EditSchedule` / `EditRoster` | 도장(stamp)은 `EditRoster` |
| ScheduleBoard | `ViewHandover` | `EditHandover` | |
| Handover / ProdReq / ProductionMeeting / Dispatch / Notice / Vendor | `ViewHandover` | `EditHandover` | |
| Checklist / Inventory / Icpms | `ViewField` | `EditField` | Icpms 일부 관리 기능은 `IsAdmin` |
| Portal / Quotation / QuotationMaster / Broken / Education / WorkAssignment | `ViewOffice` | `EditOffice` | |
| Reports (생산미팅·주간보고) | `ViewReports` | `EditReports` | |
| Material (자재물류 일정) | `ViewSchedule` | `EditSchedule` | |
| Holidays | 로그인만 | (변경 API 없음) | 공휴일 조회 전용, 민감정보 아님 |
| MesLot (MES LOT 현황 조회) | `ViewField` | (조회 전용) | 아래 "MES 권한" 참고 |

## MES 권한 — 당장은 현장(field) 영역을 빌려 쓴다

MES 화면을 포털로 옮기는 중이다. 옮긴 화면의 API 는 **현장(field)** 영역을 쓴다.

- MES 는 현장 생산 작업이라 현장 권한을 가진 사람이 곧 대상이다.
- 지금 전용 영역(`mes`)을 새로 만들면 `Users.AccessMes` 컬럼과 권한 관리 화면이 함께 필요하고,
  관리자가 등급을 넣어 주기 전까지는 **아무도** MES 에 못 들어간다.

MES 화면이 다 옮겨온 뒤 전용 영역으로 분리한다. 그때 필요한 것은 세 가지다 —
`User.AccessMes` 컬럼(SchemaUpgrader 에 추가), `Acc("ViewMes", "mes", 1)` 정책,
그리고 권한 관리 화면의 영역 한 줄. 분리 전까지는 현장 편집 권한이 곧 MES 작업 권한이다.

## 설계 의도 — 일반 직원의 업무 데이터 변경

이 시스템은 **현장 업무 시스템**이므로, 일반 직원(관리자 아님)도 담당 영역의 등급이 2면
재고·인수인계·생산요청 등을 **등록/수정할 수 있는 것이 정상 설계**다.
따라서 "관리자만 변경 가능"으로 일괄 전환하지 않는다.
관리자 전용은 **계정·권한 관리(Users)** 와 **ICP-MS 마스터 관리** 등 일부에 한정한다.

## 자료 단위 권한 — 수정과 삭제를 따로 본다

영역 등급(0/1/2)을 통과한 뒤 적용되는 **두 번째 단계**다. 판정 기준은 이름이 아니라
**작성자 계정 ID**(`CreatorUserId`)라 동명이인·개명에도 흔들리지 않는다.

| 항목 | 수정 | 삭제 |
|---|---|---|
| 공지 (Notice) | **작성자 본인 또는 관리자** | **작성자 본인 또는 관리자** |
| 인수인계 (Handover) | 등급 2 면 누구나 (공동 업무) | **작성자 본인 또는 관리자** |
| 생산요청 (ProdReq) | 등급 2 면 누구나 (조치는 담당자가 입력) | **등록자 본인 또는 관리자** |
| 회의록·주간보고 (Report) | 등급 2 면 누구나 (주/야 팀이 각자 채움) | **작성자 본인 또는 관리자** |
| 생산미팅 (ProductionMeeting, 레거시) | 등급 2 면 누구나 | **작성자 본인 또는 관리자** |

- **왜 삭제만 더 엄격한가**: 공동으로 고친 내용은 이력에 남고 되돌릴 수 있지만,
  삭제는 복구할 수 없다. 그래서 "수정은 공동, 삭제는 작성자"로 나눴다.
- **공지만 수정도 제한하는 이유**: 누가 공지했는지가 내용만큼 중요한 항목이라
  제3자가 문구를 바꾸면 책임 소재가 흐려진다.
- **작성자 미상**: 계정 ID 도 작성자 이름도 없는 과거 자료(주로 WPF 에서 온 회의록)는
  누구나 손댈 수 있다. 여기서 막으면 관리자 말고는 정리할 수 없어 업무가 멈춘다.
  새로 만드는 자료에는 항상 작성자 ID 가 들어간다.
- 화면은 서버가 내려주는 `canDelete` / `canModify` 로 버튼을 감춘다. **판단은 항상 서버가 한다.**

### 과거 자료 작성자 보정

이름만 남아 있는 기존 행은 `backfill-authors` 명령으로 계정 ID 를 채운다.
**이름이 정확히 한 사람과 일치할 때만** 채우고, 동명이인이면 건너뛰고 목록으로 출력한다.
보정 전까지는 예전처럼 이름으로 대조하므로 기존 업무는 그대로 동작한다.

## 변경 이력과 동시 수정

- **변경 이력**: 위 네 항목의 생성·수정·삭제·상태변경이 `ContentAudits` 에 남는다.
  누가(계정 ID + 실명), 언제, 무엇이 어떻게 바뀌었는지 요약이 들어간다(본문 전체는 담지 않는다).
- **동시 수정**: 자료를 불러올 때 받은 `rowVersion` 을 저장 시 그대로 돌려보낸다.
  그 사이 다른 사람이 저장했으면 **409** 로 막고 덮어쓰지 않는다.
  버전을 보내지 않으면 검사를 건너뛴다(구버전 클라이언트 호환).

## 확인이 필요한 항목 (업무 판단 필요)

1. **정책 없이 로그인만으로 열려 있는 조회 API**
   `GET /api/schedule/today-status`, `GET /api/schedule/shift-teams`, `GET /api/holidays`.
   앞의 둘은 **인수인계 대시보드·생산미팅·스케줄보드(모두 handover 영역 화면)** 가 쓰므로
   `ViewSchedule` 을 걸면 schedule 등급 0 인 인수인계 사용자의 화면이 깨진다.
   → `ViewHandover` 로 묶을지, 지금처럼 로그인만 요구할지 결정 필요.

2. **새로 확인된 미사용 레거시 API** (Recipe 와 같은 상황)
   - `ProductionMeetingController` (`/api/productionmeeting`) — 생산미팅 화면은 실제로는
     `/api/reports?type=meeting` 을 쓴다. 이 API 는 프런트에서 호출하는 곳이 없다.
   - `HolidaysController` (`/api/holidays`) — 화면은 `/api/schedule/holidays` 를 쓴다.
   → Recipe 와 같은 기준(호출자 없음 확인 후 API 만 제거, DB 데이터 보존)을 적용할지 결정 필요.
     Recipe 만 제거 지시를 받았으므로 이 둘은 그대로 두었다.

## 인증 수명 (로그인 토큰이 언제까지 유효한가)

| 상황 | 동작 |
|---|---|
| 토큰 유효기간 | `Jwt:ExpiryHours` (기본 12시간). 만료되면 재로그인. **갱신(refresh) 토큰은 두지 않는다** — 교대 근무 1회 길이와 맞고, 갱신 토큰을 저장하려면 스키마 변경이 필요하기 때문. |
| 퇴사 처리(`IsResigned = true`) | ① 로그인 자체가 거부된다(`AuthService.LoginAsync`). ② 이미 갖고 있던 토큰도 **다음 요청에서 401** (`Program.cs` 토큰 검증에서 DB 재확인). |
| 계정 삭제 | 다음 요청에서 401 (DB에 사용자가 없음). |
| 등급(권한) 변경 | 재로그인 없이 즉시 반영 (`DbPermissionHandler` 가 매 요청 DB 조회). |
| 비밀번호 변경 (본인 변경·관리자 초기화 모두) | **기존 토큰이 전부 즉시 무효**가 된다. 토큰에 비밀번호 해시의 짧은 지문(`pwv` 클레임)을 실어두고 매 요청 현재 해시의 지문과 대조한다. 본인이 변경한 창은 응답으로 받은 새 토큰으로 자동 교체되어 로그아웃되지 않는다. |
| 로그인 연속 실패 | 같은 **아이디 + IP** 조합으로 10분 내 5회 실패하면 10분간 429 응답 (`LoginThrottle`). 성공하면 즉시 초기화. 아이디만으로 잠그지 않는 이유는 남의 계정을 일부러 잠그는 악용을 막기 위해서다. |

> 사용자 조회는 요청당 1회다. 토큰 검증 단계에서 읽은 사용자를 `HttpContext.Items["auth_user"]` 에
> 넣어 `DbPermissionHandler` 가 재사용한다.

> **배포 시 주의**: `pwv` 클레임이 없는 예전 토큰은 무효로 처리된다. 이 버전을 올리면
> 접속 중이던 사용자는 **한 번 다시 로그인**해야 한다.

## 상태 코드 규약

| 상황 | 응답 |
|---|---|
| 토큰 없음 / 만료 / 퇴사·삭제·비밀번호 변경으로 무효 | 401 Unauthorized |
| 로그인했으나 등급 부족 | 403 Forbidden |
| 로그인 시도 횟수 초과 | 429 Too Many Requests (`Retry-After` 헤더 포함) |
| 등급은 있으나 남의 자료를 수정·삭제 시도 | 403 Forbidden (`ForbiddenException`) |
| 그 사이 다른 사람이 먼저 저장 | 409 Conflict (`ConcurrencyConflictException`) |
| 입력·업무 규칙 위반 | 400 Bad Request (`BusinessRuleException` 메시지 전달) |
| 대상 없음 | 404 Not Found |
| 그 외 서버 오류 | 500 (내부 메시지 비노출) |
