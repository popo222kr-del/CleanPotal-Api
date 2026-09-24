# AI 작업 공통 지침

이 저장소에서는 Codex와 Claude가 동일한 기준을 사용한다.

작업을 시작하기 전에 반드시 다음 문서를 순서대로 읽는다.

1. `docs/AI_WORKSPACE_STANDARD.md`
2. `docs/AI_HANDOFF_2026-09-24.md`

핵심 규칙:

- 정식 작업 폴더는 `C:\Users\owner\cleanpotal-api` 하나뿐이다.
- 작업 시작 시 `git status --short --branch`로 상태를 확인한다.
- dirty worktree에서 바로 `git pull`, 강제 체크아웃, reset 또는 파일 삭제를 하지 않는다.
- 비밀값은 `appsettings.local.json`에만 두며 출력·커밋·배포 덮어쓰기를 금지한다.
- 배포본은 확인된 Git 커밋에서만 만들고 `publish`, `publish_new`, `deploy_ready_final*` 같은 임시 폴더를 새 기준으로 삼지 않는다.
- 운영 데이터인 `App_Data`, 운영 설정 `appsettings.local.json`, 레거시 보존 파일 `cleanpotal.db`를 배포 중 삭제하거나 덮어쓰지 않는다.

