namespace CleanPotal.Core;

/// <summary>
/// MES 의 세부 권한 코드. 영역 등급(AccessMes 0/1/2)과 별개로, "이 사람에게 켜 줄 수 있는" 항목이다.
///
/// 등급만으로는 부족하다. 편집(2) 을 준 작업자라도 업체·제품·공정 마스터를 고치거나 이미 지나간 공정을
/// 무효화하는 것은 아무나 하면 안 된다. 그래서 MES 는 처음부터 이 여섯 가지를 따로 들고 있었다.
///
/// 이름은 MES 쪽 <c>ProductionManagement.Domain.Enums.PermissionCode</c> 와 글자까지 같아야 한다.
/// 포털(Core)은 MES 를 참조하지 않으므로 컴파일러가 잡아 주지 못한다 — 대신 테스트가 두 목록을 맞춰 본다.
/// </summary>
public static class MesPermissionCodes
{
    public const string Rollback = "Rollback";
    public const string AdminCustomer = "AdminCustomer";
    public const string AdminProduct = "AdminProduct";
    public const string AdminProcess = "AdminProcess";
    public const string AdminCertificate = "AdminCertificate";
    public const string AdminUserManagement = "AdminUserManagement";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Rollback, AdminCustomer, AdminProduct, AdminProcess, AdminCertificate, AdminUserManagement,
    };

    /// <summary>
    /// 감사 로그·안내 문구에 쓰는 한글 이름. 코드 이름(Rollback)만 남기면 나중에 로그를 읽는 사람이
    /// 무슨 권한이었는지 알 수 없다. 화면의 이름과 같은 말을 쓴다.
    /// </summary>
    public static string Label(string code) => code switch
    {
        Rollback => "공정 무효화",
        AdminCustomer => "업체 마스터",
        AdminProduct => "제품 마스터",
        AdminProcess => "공정 마스터",
        AdminCertificate => "성적서 관리",
        AdminUserManagement => "MES 사용자 관리",
        _ => code,
    };

    /// <summary>저장 형식(쉼표로 이은 코드)에서 아는 코드만 골라 낸다. 모르는 값은 버린다.</summary>
    public static HashSet<string> Parse(string? raw)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(p => All.Contains(p, StringComparer.Ordinal)))
        {
            set.Add(part);
        }
        return set;
    }

    /// <summary>저장 형식으로 되돌린다. 항상 <see cref="All"/> 순서라 같은 권한이면 같은 문자열이 된다.</summary>
    public static string Normalize(string? raw)
    {
        var set = Parse(raw);
        return string.Join(",", All.Where(set.Contains));
    }
}
