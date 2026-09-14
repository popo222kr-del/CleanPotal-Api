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
| Recipe (세정 레시피) | `ViewHandover` | `EditHandover` | 현재 화면에서 미사용(레거시 API) |
| Handover / ProdReq / ProductionMeeting / Dispatch / Notice / Vendor | `ViewHandover` | `EditHandover` | |
| Checklist / Inventory / Icpms | `ViewField` | `EditField` | Icpms 일부 관리 기능은 `IsAdmin` |
| Portal / Quotation / QuotationMaster / Broken / Education / WorkAssignment | `ViewOffice` | `EditOffice` | |
| Reports (생산미팅·주간보고) | `ViewReports` | `EditReports` | |
| Material (자재물류 일정) | 로그인만 | `EditSchedule` | **조회 정책 미적용 — 확인 필요(아래 참조)** |
| Holidays | 로그인만 | (변경 API 없음) | 공휴일 조회 전용, 민감정보 아님 |

## 설계 의도 — 일반 직원의 업무 데이터 변경

이 시스템은 **현장 업무 시스템**이므로, 일반 직원(관리자 아님)도 담당 영역의 등급이 2면
재고·인수인계·생산요청 등을 **등록/수정할 수 있는 것이 정상 설계**다.
따라서 "관리자만 변경 가능"으로 일괄 전환하지 않는다.
관리자 전용은 **계정·권한 관리(Users)** 와 **ICP-MS 마스터 관리** 등 일부에 한정한다.

## 확인이 필요한 항목 (업무 판단 필요)

1. **Material(자재물류 일정) 조회에 등급 검사 없음**
   현재는 로그인한 모든 사용자가 조회 가능하다(변경은 `EditSchedule` 필요).
   다른 영역은 조회에도 `ViewX` 를 요구하므로 일관성이 없다.
   → 자재물류 일정을 전 직원이 봐도 되는지, `ViewSchedule` 을 요구할지 결정 필요.

2. **작성자/소속팀 제한 없음**
   현재는 영역 등급만 보므로, 등급 2인 사용자는 **다른 사람이 작성한 글도 수정·삭제**할 수 있다.
   인수인계·생산요청처럼 "작성자 본인 또는 관리자만 수정"이 필요한 항목이 있는지 확인 필요.
   (필요하다면 각 엔티티의 작성자 필드를 기준으로 서버에서 추가 검사)

3. **Recipe API**
   현재 화면에서 호출하지 않는 레거시 표면이다. 계속 유지할지, 제거할지 결정 필요.

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
| 입력·업무 규칙 위반 | 400 Bad Request (`BusinessRuleException` 메시지 전달) |
| 대상 없음 | 404 Not Found |
| 그 외 서버 오류 | 500 (내부 메시지 비노출) |
