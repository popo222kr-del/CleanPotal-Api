# CleanPotal AI 인수인계 — 2026-09-24

## 먼저 할 일

Claude 또는 Codex는 새 코드 작업 전에 Git 작업 환경을 하나로 정리한다. 정식 폴더는 `C:\Users\owner\cleanpotal-api`이며, 현재 더 최신인 깨끗한 통합 커밋 `a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4`가 별도 참조 저장소에 있다.

정식 저장소에는 미커밋 변경이 있으므로 바로 `git pull`하거나 reset하지 않는다. 자세한 정리 절차는 `docs/AI_WORKSPACE_STANDARD.md`를 따른다.

## 프로젝트 개요

- 반도체 부품 세정 회사의 사내 업무 통합 관리 시스템
- WPF에서 웹으로 전환 중
- 백엔드: ASP.NET Core, 현재 프로젝트 파일 기준 `net10.0`
- 프론트엔드: React
- 사용자 약 20~30명, 사내망 사용
- 권한: `area × level`, `0=접근 불가`, `1=조회`, `2=편집`, `IsAdmin`은 전체 허용
- 담당 영역 등급 2인 일반 직원이 재고·인수인계·생산요청 등을 등록·수정하는 설계는 의도된 정책

## 코드 통합 상태

별도 통합 저장소:

```text
C:\Users\owner\Documents\Codex\2026-09-03\github-plugin-github-openai-curated-remote-3\cleanpotal-api-integration-20260921
```

통합 커밋:

```text
a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4
운영 DB 복구와 최신 Claude 변경 통합
```

포함 내용:

- Claude 커밋 `0b17460`: 첨부 파일을 DB에서 디스크로 분리
- Claude 커밋 `6ce0277`: 첨부 업로드 권한 적용
- SQLite 마이그레이션 충돌을 피하는 `DatabaseSchemaInitializer`
- 포털 파일 서비스와 실행 티켓 저장소
- 관련 테스트 및 파일 런처 도구
- 운영 DB 복구 및 최신 변경 통합

2026-09-24 현재 정식 저장소 `C:\Users\owner\cleanpotal-api`는 `cc1dd8b`이며 원격보다 2커밋 뒤이고 미커밋 변경과 여러 임시 publish 폴더가 있다. 해당 변경을 버리지 말고 `a9cc783`과 비교해 정식 저장소에 통합해야 한다.

## 배포 장애와 해결 내용

### 발생했던 문제

- IIS `HTTP 500.30` 발생
- 구버전 DLL이 SQLite를 고정 사용
- 기존 `ScheduleBlocks` 테이블을 다시 만들려 하며 `table already exists`로 종료
- 개발 PC의 여러 publish 폴더 중 잘못된 구버전을 서버에 복사

### 검증된 배포본

```text
C:\Users\owner\Documents\Codex\2026-09-03\github-plugin-github-openai-curated-remote-3\artifacts\CleanPotal-v5-20260921
```

검증 해시:

```text
CleanPotal.Api.dll
4FA4C7B3901404D39D17E3FC2C311FFA94D9263994B8376D98F5885E5516F673

CleanPotal.Infrastructure.dll
F23BC929D4582CB8BC442E4DD5CE2B28DF8A61910EB58F056D7D92BECCF5AED9
```

운영 배포 경로는 `C:\Webjueon\publish`이다. 배포 중 `appsettings.local.json`, `App_Data`, `cleanpotal.db`를 보존해야 한다.

## DB 상태

- 운영은 SQL Server 사용
- 서버: `10.10.40.61`
- 데이터베이스: `JUEON`
- 자격 증명은 운영 `appsettings.local.json`에만 존재하며 문서나 Git에 기록하지 않음
- 정상 시작 로그는 `[db] SQL Server 사용`
- SQLite 파일은 레거시·복구용으로 보존하지만 운영 데이터 원본으로 사용하지 않음

## 온·습도 MQTT 구성

### 현재 구성

```text
SONOFF 센서 3대(9/26 기준 4대 — dongtan_1~4, appsettings.json 참고)
  → Zigbee2MQTT
  → Mosquitto on 10.10.10.13:1883
  → 테스트 웹 / 운영 웹
```

역할:

| 주소 | 역할 |
|---|---|
| `10.10.10.13` | 테스트 서버 + Zigbee2MQTT + Mosquitto |
| `10.10.10.119` | 운영 IIS 웹 서버 |

Zigbee2MQTT 설정:

```yaml
mqtt:
  server: mqtt://127.0.0.1:1883
  base_topic: zigbee2mqtt
```

장치 friendly name:

```text
dongtan_1
dongtan_2
dongtan_3
```

확인 완료 사항:

- Zigbee2MQTT 화면에서 센서 3대 실시간 수신
- `mosquitto_sub -h 127.0.0.1 -p 1883 -t 'zigbee2mqtt/#' -v`에서 `bridge/state` 수신
- 테스트 화면에서 MQTT 연결 및 센서 `3/3`, 최신 온도·습도 표시
- 운영 서버 `10.10.10.119`에서 `Test-NetConnection 10.10.10.13 -Port 1883` 성공

`10.10.10.13`의 Mosquitto 설정에는 다음 listener가 추가됐다.

```text
listener 1883 0.0.0.0
allow_anonymous true
```

Windows 방화벽은 운영 서버 `10.10.10.119`에서 오는 TCP 1883만 허용하도록 구성했다. 현재 `allow_anonymous true`는 사내망과 IP 제한에 의존하는 임시 구성이다. 운영 안정화 후 MQTT 사용자/비밀번호 또는 TLS/VPN 적용을 검토한다.

### 아직 최종 확인이 필요한 작업

운영 서버의 `C:\Webjueon\publish\appsettings.local.json`에 기존 JWT·DB 설정을 유지한 채 다음 값을 추가 또는 수정한다.

```json
"Zigbee": {
  "Mqtt": {
    "Enabled": true,
    "Host": "10.10.10.13",
    "Port": 1883,
    "TopicPrefix": "zigbee2mqtt"
  }
}
```

이후 `Cleanjueon` 앱 풀을 재활용하고 운영 온·습도 화면에서 다음을 확인한다.

- MQTT 정상
- Zigbee2MQTT 정상
- 센서 3/3
- 세 센서의 최신 수신 시간이 갱신됨

## 다음 작업 우선순위

1. 정식 저장소와 통합 커밋 `a9cc783`을 안전하게 합쳐 GitHub에 단일 기준 브랜치를 만든다.
2. 운영 서버 MQTT Host를 `10.10.10.13`으로 적용하고 실제 화면을 검증한다.
3. 표준 `artifacts\publish` 빌드 흐름을 만들고 임시 publish 폴더 사용을 중단한다. → (9/26 정리) 표준은 저장소의 `.\publish` — `tools\deploy-test.ps1` 이 만들고 `배포하기.cmd` 로 운영에 올린다.
4. MQTT 익명 접속을 계정 인증 또는 TLS/VPN 구성으로 강화한다.
5. K-System 휴가 승인 완료 데이터를 일정표에 자동 반영하는 연동 방식은 아직 조사·구현 전이다. 공식 API, DB 조회 권한, 파일 내보내기 또는 RPA 가능 여부를 K-System 공급사에 먼저 확인해야 한다.

## Claude에게 바로 보낼 프롬프트

```text
C:\Users\owner\cleanpotal-api를 유일한 정식 작업 폴더로 사용해 주세요.
먼저 CLAUDE.md, docs/AI_WORKSPACE_STANDARD.md, docs/AI_HANDOFF_2026-09-24.md를 끝까지 읽고 git status --short --branch를 확인하세요.

현재 정식 worktree는 원격보다 2커밋 뒤이고 미커밋 변경이 있으므로 바로 git pull, reset, checkout 또는 파일 삭제를 하면 안 됩니다. 별도 통합 저장소 C:\Users\owner\Documents\Codex\2026-09-03\github-plugin-github-openai-curated-remote-3\cleanpotal-api-integration-20260921의 커밋 a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4에 최신 Claude 변경과 운영 복구 변경이 깨끗하게 통합돼 있습니다.

첫 작업으로 현재 변경을 보존하고 a9cc783과 파일별로 비교한 뒤 정식 저장소와 GitHub에 하나의 기준 브랜치로 통합해 주세요. 비밀값은 출력하거나 커밋하지 말고, appsettings.local.json, App_Data, cleanpotal.db를 보존하세요. 통합 후 테스트 결과와 최종 커밋, 표준 publish 경로를 보고해 주세요.

그다음 운영 서버의 MQTT 설정이 Host=10.10.10.13, Port=1883, TopicPrefix=zigbee2mqtt인지 확인하고 앱 풀 재활용 후 운영 화면에서 MQTT/Zigbee2MQTT/센서 3/3을 검증해 주세요. 서버 비밀번호나 JWT 키는 절대 출력하지 마세요.
```

