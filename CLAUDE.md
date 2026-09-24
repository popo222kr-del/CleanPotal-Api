# Claude 작업 시작 지침

이 프로젝트의 정식 작업 폴더는 `C:\Users\owner\cleanpotal-api`이다.

코드를 수정하기 전에 반드시 다음 문서를 읽고 현재 Git 상태를 확인한다.

1. `docs/AI_WORKSPACE_STANDARD.md`
2. `docs/AI_HANDOFF_2026-09-24.md`
3. `git status --short --branch`

현재 작업 폴더는 원격보다 뒤처진 커밋과 미커밋 변경이 함께 있으므로 바로 `git pull`하지 않는다. 별도 Codex 통합 저장소의 커밋 `a9cc7837a9bbf46ead0d489a7cfc338fbd5e37b4`를 기준으로 기존 변경을 보존하며 정식 저장소에 통합하는 것이 첫 작업이다.

비밀값이나 연결 문자열 전체를 화면에 출력하지 않는다. 운영 배포 시 `appsettings.local.json`, `App_Data`, `cleanpotal.db`를 보존한다.

