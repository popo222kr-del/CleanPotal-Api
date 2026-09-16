using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.Entities;

// LOT - 이 시스템의 중심 엔티티. "고객이 맡긴 제품 한 묶음"이며, 전산등록으로 태어나 공정을 하나씩
// 통과하다가 고객출하로 끝난다.
// 현재 위치는 CurrentProcessDefinitionId(어느 공정), 현재 형편은 CurrentStatus(대기/진행중/HOLD 등)가
// 들고 있고, 지나온 자취는 ProcessHistory에 한 줄씩 쌓인다. 갈 길은 ProcessRouteId가 정한다.
// 수량 변화는 QuantityTransaction, 검사값은 InspectionRecord로 각각 따로 남는다.
public class Lot : Entity<int>
{
    // 사용자에게 보이는 관리번호. LotId(PK)와 분리하며, 사용자가 직접 입력하지 않고 SystemSequence로만 발급한다.
    public string LotNumber { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int ReceivedQuantity { get; set; }

    public int CurrentProcessDefinitionId { get; set; }
    public ProcessDefinition CurrentProcessDefinition { get; set; } = null!;

    public LotStatus CurrentStatus { get; set; }

    // 이 Lot이 실제로 따라가는 공정 경로. 공정 완료 시 "다음 단계"를 계산하거나 State Transition을
    // 검증할 때 전체 ProcessRouteStep이 아니라 이 Route에 속한 Step만 봐야 한다 (Phase 14부터 Route가
    // STANDARD/SIMPLE 등으로 여러 개가 되므로 필수).
    public int ProcessRouteId { get; set; }
    public ProcessRoute ProcessRoute { get; set; } = null!;

    // 이 Lot을 만든 전산등록 레코드. LotService.CreateAsync처럼 Registration을 거치지 않고 생성되는
    // 예외적인 경로가 남아있을 수 있어 nullable로 둔다 (신규 전산등록 화면에서 생성되는 Lot은 항상 값이 있다).
    public int? RegistrationId { get; set; }
    public Registration? Registration { get; set; }

    // 같은 ItemCode를 가진 다른 Lot들을 이 Lot 아래로 묶을 때 사용 (자기 자신을 대표로 가리키지 않음 -
    // 대표 Lot 본인은 null로 둔다). 특정 품목에 한정하지 않고 언제든 사후 지정 가능.
    public int? RepresentativeLotId { get; set; }
    public Lot? RepresentativeLot { get; set; }

    // 전산등록 시 자동 생성(ExportNumber_순번)되고, 각인이 실물과 다르면 수기로 수정 가능.
    // 이 값은 "현재(반입) S/N"이다 - 실물 각인이 바뀌면 이 값이 갱신된다.
    public string SerialNumber { get; set; } = string.Empty;

    // 2026-09-08 지시: "반출 S/N" - 전산등록 시점에 부여된 최초 S/N을 그대로 보존한다. SerialNumber는
    // 실물 각인이 달라지면 수정될 수 있어, 세정 이력 조회에서 최초값과 현재값을 함께 보여주기 위해
    // 별도로 남긴다. 기존 데이터에는 값이 없으므로 null 허용(마이그레이션에서 채우지 않는다).
    public string? InitialSerialNumber { get; set; }

    public DateTime ReceivedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    // 동시성 검증용 (CLAUDE.md 8번: 동일 Lot 동시 처리 차단 - UI Disable만으로 해결하지 않음).
    // SQL Server 전용 ROWVERSION 대신, SQLite/SQL Server 양쪽에서 동일하게 동작하도록 애플리케이션이
    // SaveChanges 시점에 직접 증가시키는 정수 버전 토큰을 사용한다 (ApplicationDbContext 참고).
    public int Version { get; set; }
}
