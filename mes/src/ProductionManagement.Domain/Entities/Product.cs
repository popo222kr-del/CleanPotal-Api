namespace ProductionManagement.Domain.Entities;

// 제품 마스터. 유일 식별 키는 CleaningCode(=MAT ID)다 - ProductCode/ItemCode가 아니다.
//   CleaningCode 세정코드. 고객사가 부여하는 고유 관리번호이며 LOT은 이 코드로 제품을 찾는다.
//   ItemCode     품목코드(도면명/규격). 사람이 부르는 이름이라 서로 다른 제품이 겹칠 수 있다.
//   ProductCode  "제품 규격" 텍스트(이름만 옛것). SerialNumber는 "제품 단가"다 - 아래 주석 참고.
// 이 제품이 어떤 공정을 어떤 레시피로 거치는지는 ProductProcessFlow / ProductRecipeAssignment /
// ProductParameterAssignment가 각각 들고 있고, "셋업 > 제품 셋업" 화면에서 관리한다.
public class Product : Entity<int>
{
    // 컬럼/속성 이름은 예전 그대로지만(마이그레이션 부담 회피), 화면 표시 이름은 "제품 규격"이다
    // (2026-08-21 피드백 - 실제로는 식별 코드가 아니라 규격 텍스트로 쓰인다는 사용자 확인 후 전환).
    // 유일값 제약을 걸어두면 서로 다른 제품이 같은 규격을 가질 때 저장이 막히므로, 이 전환과 함께
    // DB Unique 인덱스도 제거했다(ProductConfiguration.cs) - 진짜 유일 식별 키는 CleaningCode다.
    public string ProductCode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    // 품목 구분(2026-08-25 신설) - 세정코드별 제품을 큰 범주(예: Accessory/QTZ Boat/Inner Tube 등)로
    // 묶는 분류값. "입 · 출고 현황 조회" 재공현황 매트릭스의 "MAT GROUP" 컬럼에 그대로 표시된다.
    // 선택 입력(없으면 매트릭스에 "-"로 보인다).
    public string? ItemCategory { get; set; }

    // 컬럼/속성 이름은 예전 그대로지만, 화면 표시 이름은 "제품 단가"다(2026-08-21 피드백 - 이 필드는
    // 원래도 Product 어디에서도 조회 키로 쓰이지 않는 선택 정적 필드였다). S/N이 없는 제품도 지원해야
    // 하므로 nullable (CLAUDE.md 7번). LOT 단위로 자동채번되는 Lot.SerialNumber와는 다른 필드다.
    public string? SerialNumber { get; set; }

    // 품목의 기본 식별자(참조 화면 "제품 셋업"의 좌측 목록 키와 동일한 역할). 업체별 품목코드보다
    // 우선하는 기준 키로 승격되어 필수+유일값이다 - 전산등록에서 세정코드를 입력하면 이 값으로 품목을
    // 찾아 업체/품목명을 자동으로 채운다.
    public string CleaningCode { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    // 기본 LINE(고객사 LINE 정의 Master Data 참조). 아직 전산등록 화면의 LINE 입력란은 자유 텍스트라
    // 이 값이 자동으로 채워지진 않는다 - 제품 셋업에서 "기준정보"로 지정만 해두는 단계.
    public int? DefaultLineId { get; set; }
    public LineDefinition? DefaultLine { get; set; }

    // 2026-08-26: 세정코드별 제품 이미지(바이트로 DB 저장). "제품 단가/이미지" 탭에서 등록한다.
    public byte[]? ImageData { get; set; }

    // 2026-08-27: 세정코드별 성적서 기본 양식(Excel). PM→성적서 병합 - 전산등록 시 이 양식을 기준으로 LOT별
    // 성적서를 생성한다. DRM Excel이라 바이트로 저장했다가 생성 시 임시 파일로 풀어 Excel COM으로 연다.
    public byte[]? CertificateTemplateData { get; set; }
    public string? CertificateTemplateFileName { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
