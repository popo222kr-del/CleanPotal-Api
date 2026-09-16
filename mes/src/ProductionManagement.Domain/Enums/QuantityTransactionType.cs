namespace ProductionManagement.Domain.Enums;

// 수량 증감 이력(QuantityTransaction)의 종류. LOT 수량이 왜 바뀌었는지를 남겨서, 최종 수량만이 아니라
// 그 과정을 되짚을 수 있게 한다.
//   Received  입고된 수량            Good     양품 판정
//   Defect    불량 판정              Rework   재작업으로 되돌린 수량
//   Scrap     폐기                   Shipped  고객 출하
//   Remaining 잔량 보정(실사 등으로 남은 수량을 맞출 때)
public enum QuantityTransactionType
{
    Received = 0,
    Good = 1,
    Defect = 2,
    Rework = 3,
    Scrap = 4,
    Shipped = 5,
    Remaining = 6
}
