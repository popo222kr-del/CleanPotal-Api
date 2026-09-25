# 첨부 파일 저장 위치 (NAS)

웹에서 올리는 사진·파일(체크시트, BROKEN, 주간보고, 기타세정 현황, 생산팀 요청사항)은 모두 **첨부 보관소** 한 곳에 파일로 저장된다. DB 에는 위치와 이름만 한 줄 남는다.

## 1. 폴더 구조

```
<Storage:AttachmentsPath>\
  체크시트\2026-09\20260925_143205_주간_M-OUT 출고검사실_M-012_NG.jpg
  BROKEN\2026-09\20260925_151000_(올린 파일 이름).jpg
  주간보고\2026-09\...
  기타세정\2026-09\20260925_090000_(업체)_작업.jpg
  생산팀요청\2026-09\20260925_100000_(분류)_(위치)_요청.jpg
```

- 이름 = `날짜_시각_설명`. 시각은 **서버가 받은 시각**(휴대폰에서 줄이면서 촬영 정보가 사라진다).
- 같은 초·같은 이름이면 `_2`, `_3` 을 붙인다. 덮어쓰지 않는다.
- **탐색기에서 옮기거나 이름을 바꾸면 화면에서 못 연다**(DB 에 적힌 위치로 찾는다). 지울 때도 화면에서 지운다.
- 받을 때 파일 이름은 올린 사람의 원래 이름 그대로 내려준다.

## 2. 설정 (서버 `appsettings.local.json` — Git 에 올리지 않는다)

```json
"Storage": {
  "AttachmentsPath": "\\\\10.10.40.98\\천안공장\\25. 생산 Inform 자료\\주언\\Clean_Data",
  "ShareUser": "NAS계정",
  "SharePassword": "NAS비밀번호"
}
```

- JSON 안에서는 `\` 를 `\\` 로 두 번 쓴다.
- NAS 는 서버 PC 의 Windows 계정을 모르므로(IIS 앱 풀 계정도 마찬가지) 포털이 `ShareUser` 계정으로 직접 연결을 연다(`NetworkShare`). 비밀번호는 로그에 찍지 않는다.
- 계정 이름에 NAS 이름이 필요하면 `NAS이름\\계정` 처럼 쓴다.
- `MesData:RootPath` 도 같은 NAS 공유 밑이면 같은 계정으로 열린다(예: `...\\Clean_Data\\MES`).
- 설정이 없으면 예전처럼 앱 폴더의 `App_Data\attachments` 를 쓴다.

기동 로그(`App_Data\logs\portal-날짜.log`)에서 확인한다.

| 로그 | 뜻 |
|---|---|
| `[storage] 공유폴더 연결: \\10.10.40.98\천안공장 (계정 …)` | NAS 로그인 성공 |
| `[storage] 첨부 저장 위치: … — 쓰기 확인됨` | 정상 |
| `[storage][오류] … NAS 계정 또는 비밀번호가 틀립니다` | ShareUser/SharePassword 확인 |
| `[storage][오류] 첨부 저장 위치에 쓸 수 없습니다` | 그 계정에 `Clean_Data` 쓰기 권한이 없음 |

## 3. 처음 옮길 때 순서

1. NAS 에 포털 전용 계정을 만들고 `Clean_Data` 에만 쓰기 권한을 준다. 다른 사람은 읽기 전용(또는 접근 불가) 권장 — 공유폴더를 열 수 있는 사람은 첨부를 보고 지울 수 있다.
2. 서버에서 먼저 손으로 확인: `net use \\10.10.40.98\천안공장 /user:계정 *` → `Clean_Data` 에 파일 하나 만들어 보기 → `net use \\10.10.40.98\천안공장 /delete`.
3. 배포(앱 풀 중지 → 교체 → **시작 전에**) 기존 파일을 NAS 로 복사:
   ```
   robocopy "C:\Webjueon\publish\App_Data\attachments" "\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\Clean_Data" /E /COPY:DT /R:2 /W:2
   ```
   (위 `net use` 로 연결해 둔 상태에서. 예전 `yyyyMM` 폴더가 그대로 복사된다.)
4. `appsettings.local.json` 에 `Storage` 를 넣고 앱 풀 시작 → 로그에서 "쓰기 확인됨" 확인.
5. 정리 명령(아래 4번) — 미리보기로 개수·용량을 먼저 본다.

## 4. 정리 명령 `migrate-attachments`

```
cd C:\Webjueon\publish
dotnet CleanPotal.Api.dll migrate-attachments --dry-run   # 보기만
dotnet CleanPotal.Api.dll migrate-attachments             # 실제로
```

하는 일:
1. **DB 칸 안의 사진(base64)을 파일로 꺼낸다** — 기타세정 현황·생산팀 요청사항(최근까지 DB 에 통째로 저장), 주간보고·BROKEN 의 옛 기록. 칸에는 `att:번호|이름|종류` 만 남는다. 파일 이름은 그 기록의 작성일 기준.
2. **옛 이름(`yyyyMM\GUID.jpg`) 첨부를 새 규칙으로 옮긴다** — 올린 날짜·원래 이름으로.

- 사이트가 켜져 있어도 된다. 칸을 바꾸는 순간 누가 그 기록을 고쳤으면 그 기록은 건너뛴다. 여러 번 돌려도 남은 것만 한다.
- "파일 없음" 이 나오면 3번 robocopy 를 빠뜨린 것이다.
- DB 용량은 사진을 꺼낸 뒤에도 파일 크기가 바로 줄지 않는다(SQL Server 는 빈 공간을 잡아 둔다). 꼭 줄이려면 SSMS 에서 DB → 태스크 → 축소. 10GB 제한은 "쓰는 공간" 기준이라 축소하지 않아도 여유는 생긴다.

## 5. 테스트 서버

테스트 서버(`tools/deploy-test.ps1`, `C:\cleanpotal-test`)도 운영 DB 를 같이 쓰므로, 테스트 서버의 `appsettings.local.json` 에도 **같은 `Storage` 설정**을 넣어야 테스트에서 올린 사진이 운영 화면에서도 열린다. 넣지 않으면 개발 PC 디스크에 저장된다. `migrate-attachments` 는 운영 서버에서만 실행한다.
