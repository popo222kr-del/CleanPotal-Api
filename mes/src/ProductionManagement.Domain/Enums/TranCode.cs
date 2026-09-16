namespace ProductionManagement.Domain.Enums;

// "기타프로그램 마스터 데이터.xlsx"의 TRAN 정의 시트 기준. CREATE(입고 이전 전산등록)와 LASER는
// TRAN 사용 공정 시트의 실제 46개 전이 행 어디에도 TRAN CODE로 쓰이지 않아 이 엔진에서는 정의하지 않는다.
public enum TranCode
{
    Start = 0,
    End = 1,
    Hold = 2,
    Release = 3,
    Rework = 4,
    Skip = 5,
    Ship = 6
}
