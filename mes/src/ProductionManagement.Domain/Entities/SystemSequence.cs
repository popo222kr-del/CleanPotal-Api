namespace ProductionManagement.Domain.Entities;

// Lot Number는 이 테이블의 CurrentValue를 Transaction으로 증가시켜서만 발급한다.
// MAX(LotNumber)+1 방식은 절대 사용하지 않는다 (CLAUDE.md 7번 / 절대 금지사항 4번).
public class SystemSequence : Entity<int>
{
    public string SequenceName { get; set; } = string.Empty;
    public long CurrentValue { get; set; }
}
