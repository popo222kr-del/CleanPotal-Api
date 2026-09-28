namespace CleanPotal.Core.DTOs;

public record VendorDto(
    int Id, string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string BasePath, string LinkUrl, string Addresses, string Managers,
    int? MesCustomerId,
    // 등록 부서(조직도 Id·이름). 같은 업체라도 부서마다 따로 등록한다.
    int? DeptId = null, string DeptName = "");

public record VendorUpsertRequest(
    string VendorName, string Category, bool IsWeekly, bool IsFavorite,
    string? BasePath, string? LinkUrl, string? Addresses, string? Managers,
    int? MesCustomerId,
    // 등록 부서 — 관리자만 고를 수 있다(그 밖에는 본인 부서로 정해진다).
    int? DeptId = null);
