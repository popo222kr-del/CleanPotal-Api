using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.DTOs;

// AETS 출하검사 성적서(Excel)의 "앱 연동 영역"에 채워 넣을 논리 데이터 한 세트.
//  - Part Information(섹션1) / Visual Inspection Data(섹션2) / Measuring Data(섹션3)까지만 앱이 채운다.
//    그 아래(ICP-MS, Measuring Point 등)는 사용자가 자유롭게 쓰는 영역이라 앱이 건드리지 않는다.
//  - 여기에는 "논리 값"만 담고, 실제 셀 주소 매핑은 Certificate 프로젝트의 AETS 필러가 안다
//    (템플릿 골조가 바뀌면 그 한 곳만 고치면 되도록 관심사를 분리).
public record CertificateFillData(
    // ── 섹션1: Part Information ─────────────────────────────
    // 2026-08-31 피드백(#7): 성적서 헤더 매핑을 실제 요구에 맞춰 재정의한다.
    //  - Customer(셀)   = 마스터 LINE 명(LineDefinition.Description)   → LineName
    //  - Unit Maker(셀) = 전산등록 PM 설비명(업체명, Registration.PmEquipmentName) → UnitMaker
    //  - LINE(셀)       = 마스터 LINE의 USER 코드(LineDefinition.UserCode)에서 앞의 "I" 제외 → LineUserCode
    //  - Code(셀)       = 전산등록 분임조(Registration.TeamName)        → TeamCode  ※실제 셀 주소는 사용자 확인 필요
    //  - M3:M5 병합(담당 인원) = 7000 출고검사 작업자 이름              → OutgoingInspector
    string LineName,        // Customer 셀    = 마스터 LINE 명(LineDefinition.Description)
    string LineUserCode,    // LINE 셀        = 마스터 LINE USER 코드(앞 "I" 제외)
    string UnitMaker,       // Unit Maker 셀  = 전산등록 PM 설비명(업체명)
    string TeamCode,        // Code 셀        = 전산등록 분임조
    string OutgoingInspector,// M3:M5 병합    = 7000 출고검사 작업자 이름
    string PartName,        // C8  제품명        = Product.ProductName
    string CleaningCode,    // C9  세정 코드     = Product.CleaningCode
    int Quantity,           // C10 수량         = Lot.ReceivedQuantity
    string ItemCode,        // K7  품목코드      = Product.ItemCode
    string Process,         // K8  Process      = Registration.ProcessLabel
    string SerialNumber,    // K9  S/N          = Lot.SerialNumber
    DateTime InspectionDate,// K10 최종 성적서 수정 일자 = 채우는 시점
    // (C11 케미칼 횟수 · K11 산세정 진행품 등은 수기 기입/고정이라 앱이 채우지 않는다)
    // ── 섹션2·3: 검사 파라미터 값(입고 2100 / 출고 7000) ──────
    // (Code, Oper) 조합으로 필러가 찾아 해당 셀에 쓴다. 값이 없는 항목은 목록에 없다.
    IReadOnlyList<CertificateInspectionValue> Values);

// 검사값 한 건. Oper = "2100"(입고) / "7000"(출고). Numeric은 Min/Max로 SPEC 표기까지 채운다.
// CertificateLabel = 성적서 표기명(마스터). 성적서 항목명과 매칭할 때 Code 대신 이 값을 우선한다.
public record CertificateInspectionValue(
    string Code,
    string Oper,
    string? InputValue,
    decimal? MinValue,
    decimal? MaxValue,
    ParameterType ParameterType,
    string? CertificateLabel = null);
