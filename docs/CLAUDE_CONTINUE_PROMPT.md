# Claude 이어서 작업 요청문

아래 내용을 Claude Code에 그대로 전달한다.

```text
C:\Users\owner\cleanpotal-api를 유일한 정식 작업 폴더로 사용해 주세요.

코드를 수정하기 전에 다음 파일을 끝까지 읽으세요.
- CLAUDE.md
- docs/AI_WORKSPACE_STANDARD.md
- docs/AI_HANDOFF_2026-09-24.md

그다음 git status --short --branch와 최근 커밋을 확인하세요. 현재 정식 worktree는 원격보다 2커밋 뒤이고 미커밋 변경과 미추적 소스가 있으므로 바로 git pull, reset, checkout, clean 또는 파일 삭제를 하면 안 됩니다.

별도 통합 저장소 C:\Users\owner\Documents\Codex\2026-09-03\github-plugin-github-openai-curated-remote-3\cleanpotal-api-integration-20260921의 커밋 a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4에 최신 Claude 변경과 운영 복구 변경이 통합돼 있습니다.

첫 작업은 현재 변경을 보존하고 a9cc783과 파일별로 비교한 뒤, C:\Users\owner\cleanpotal-api와 GitHub에 하나의 기준 브랜치로 안전하게 통합하는 것입니다. 비밀값은 출력하거나 커밋하지 말고 appsettings.local.json, App_Data, cleanpotal.db를 보존하세요. 완료 후 최종 브랜치, 커밋, 테스트 결과와 표준 publish 경로를 보고하세요.

그다음 운영 서버 10.10.10.119의 MQTT 설정이 Host=10.10.10.13, Port=1883, TopicPrefix=zigbee2mqtt인지 확인하고, Cleanjueon 앱 풀 재활용 후 운영 온·습도 화면에서 MQTT/Zigbee2MQTT/센서 3/3과 최신 수신 시간을 검증하세요. JWT 키, SQL 비밀번호, MQTT 비밀번호는 절대 출력하지 마세요.
```

