namespace ProductionManagement.Application.Interfaces;

// 2026-08-20 "이력 삭제"(TRAN 무효화) - 예전 Rollback(공정/상태를 관리자가 임의로 골라 강제 이동)을
// 완전히 대체한다. TRAN 엔진 도입 이후 정상적인 "뒤로가기"는 Rework TRAN이 처리하므로, Rollback이
// 원래 하던 일은 대부분 필요 없어졌다 - 남은 유일한 실사용 시나리오는 "작업자가 실수로 실행한 TRAN
// 하나를 되돌리는" 것이라, 그 범위로만 좁혀서 새로 만들었다. ProcessHistory 행을 DB에서 지우지 않고
// (CLAUDE.md 절대 금지사항: 이력 삭제 금지) "무효화됨"만 표시한다 - 관리자 전용, 자세한 제약 조건은
// ProcessHistoryVoidService.cs 참고.
public interface IProcessHistoryVoidService
{
    Task VoidAsync(int processHistoryId, string reason, CancellationToken cancellationToken = default);
}
