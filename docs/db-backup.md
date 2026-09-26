# DB 백업과 복원

포털 자료는 모두 DB 서버(10.10.40.61, SQL Server Express)의 `JUEON` DB 에 있다. 이 문서 전에는 DB 백업이 없었다 — **운영 배포 백업(`C:\Webjueon\backup`)은 프로그램 파일만** 담고 DB·`App_Data` 는 담지 않는다.

## 1. 처음 한 번 (운영 서버 10.10.10.119)

1. 새 publish 를 배포하면 `C:\Webjueon\publish\DB백업하기.cmd` 가 생긴다.
2. 더블클릭 → 관리자 확인 "예" → 백업이 끝나면 `[backup] ✅ …\JUEON_Sat.bak (DB 서버 PC) — 읽기 확인됨` 이 보인다.
3. "매일 03:30 자동 백업을 등록할까요?" 에 `Y`. 작업 스케줄러에 **CleanPotal DB Backup** 이 생긴다(04:00 앱 풀 재시작 전).

| 결과 | 뜻 / 할 일 |
|---|---|
| `✅ …JUEON_요일.bak` | 정상. 파일은 **DB 서버 PC** 의 SQL Server 기본 백업 폴더에 있다 |
| `포털 DB 계정에 백업 권한이 없습니다` | DB 관리자에게 포털 계정(JJUEON)에 `db_backupoperator` 권한 요청 |
| `기본 백업 폴더를 알 수 없습니다` / `운영 체제 오류` | DB 서버 안의 폴더를 직접 준다: `dotnet CleanPotal.Api.dll backup-db --dir "D:\Backup"` (예약 작업의 명령에도 같은 인자) |

- 요일별 7개 파일(`JUEON_Mon.bak` … `JUEON_Sun.bak`)을 돌려 덮어쓴다 → 지우는 작업 없이 최근 1주일. 매월 1일 것은 `JUEON_2026-10.bak` 으로 따로 남는다(월별 파일은 가끔 손으로 정리).
- 기록: `C:\Webjueon\publish\App_Data\logs\db-backup.log`. 작업 스케줄러의 "마지막 실행 결과" 가 `0x0` 이 아니면 실패.
- DB 크기도 같이 찍힌다(`데이터 파일 N MB (한도 10240 MB)`). 8000 MB 를 넘으면 정리를 검토한다.

## 2. 백업을 DB 서버 밖으로도

백업 파일이 DB 서버 PC 안에만 있으면 그 PC 가 고장 날 때 같이 잃는다. DB 서버 담당자와 둘 중 하나를 정한다.
- DB 서버의 백업 폴더를 공유해 주면, 운영 서버 작업에 `robocopy \\10.10.40.61\공유 \\NAS\…\DB_Backup *.bak /R:2 /W:5` 를 덧붙인다.
- 또는 DB 서버에서 직접 NAS 로 복사하는 예약 작업.

**첨부 사진**: NAS 로 옮기기 전까지는 `C:\Webjueon\publish\App_Data\attachments` 에 있고 배포 백업에서도 빠진다. 옮긴 뒤에는 NAS 의 스냅샷/백업 정책을 따른다.

## 3. 복원

> 복원은 DB 를 **통째로 그 시점으로 되돌린다**. 그 뒤에 입력한 자료는 사라진다.

**먼저 연습**(권장, 한 번은 꼭): SSMS → 데이터베이스 → 데이터베이스 복원 → 장치 → `.bak` 선택 → **대상 데이터베이스 이름을 `JUEON_복원확인`** 으로 바꿔 복원 → 표 몇 개를 열어 본 뒤 삭제. 이렇게 하면 운영 DB 는 건드리지 않고 백업이 쓸 만한지 확인된다.

**실제 복원**:
1. 운영 서버에서 앱 풀 중지: `%windir%\system32\inetsrv\appcmd stop apppool /apppool.name:Cleanjueon`
2. 테스트 서버(개발 PC `C:\cleanpotal-test`)도 끈다 — 같은 DB 를 쓴다.
3. 복원 직전 상태도 남긴다: `DB백업하기.cmd`(오늘 요일 파일을 덮어쓰니, 되돌릴 파일이 오늘 요일 것이면 먼저 다른 이름으로 복사해 둔다).
4. SSMS → `JUEON` → 태스크 → 복원 → 데이터베이스 → 장치에서 `.bak` → 옵션 "기존 데이터베이스 덮어쓰기(WITH REPLACE)" + "대상 데이터베이스에 대한 기존 연결 닫기" → 확인.
5. 앱 풀 시작: `appcmd start apppool /apppool.name:Cleanjueon` → 포털 접속 확인.

## 4. 자료를 크게 바꾸기 전에는 백업 먼저

- `migrate-attachments`(사진을 DB 칸 → 파일로 옮김)는 **되돌릴 수 없다**. 옮긴 뒤에는 그 이전 버전 프로그램으로 되돌리면 사진이 안 보인다. 실행 전에 `DB백업하기.cmd`.
- `refresh-from-wpf`, `rebuild-from-wpf` 는 DB 를 비우거나 다시 만든다. 대상 DB 이름을 한 번 더 적어야 실행된다: `… refresh-from-wpf "폴더" --confirm JUEON`. 테스트 서버·개발 PC 도 운영 DB 를 가리키니 특히 조심.
