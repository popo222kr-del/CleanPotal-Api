using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// Lot 수량은 이 테이블에 Row를 추가하는 방식으로만 변경한다. Lot의 수량 필드를 직접 UPDATE하지 않는다 (CLAUDE.md 7번).
public class QuantityTransaction : Entity<int>
{
    public int LotId { get; set; }
    public Lot Lot { get; set; } = null!;

    public QuantityTransactionType TransactionType { get; set; }
    public int Quantity { get; set; }
    public DateTime OccurredAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}
