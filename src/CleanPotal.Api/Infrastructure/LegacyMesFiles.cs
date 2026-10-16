using System.Text.RegularExpressions;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 기존 MES 파일 서버(<c>\\10.10.40.98\nas\00.MESServer</c>) 읽기 전용 — 2026-10-16 확인한 규칙:
/// <list type="bullet">
/// <item>제품 사진 = <c>MatImage\{제품 코드}.jpg</c></item>
/// <item>LOT 성적서 = <c>Inspection_PDF\{LOT ID}.pdf · .xlsx</c>, <c>Inspection\{LOT ID}.*</c>(또는 그 이름의 폴더 안)</item>
/// <item>성적서 양식 = 제품 사용자 정의 필드 3(성적서 파일명) — 양식 폴더 후보에서 찾는다</item>
/// </list>
/// 폴더는 설정(LegacyMes:FileRoot · CertDirs · CertTemplateDirs)으로 바꿀 수 있다. 이름은 DB·주소에서 오므로
/// 경로 조각(.. · \ · /)이 섞이면 찾지 않는다. NAS 가 안 닿으면 '없음'으로 답한다(화면은 그대로 연다).
/// </summary>
public sealed class LegacyMesFiles
{
    public string Root { get; }
    private readonly string[] _certDirs;
    private readonly string[] _templateDirs;

    private static readonly Regex SafeId = new(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,59}$");
    private static readonly string[] ImageExt = [".jpg", ".jpeg", ".png", ".bmp", ".gif"];
    private static readonly string[] CertExt = [".pdf", ".xlsx", ".xlsm", ".xls"];

    public LegacyMesFiles(IConfiguration cfg)
    {
        Root = string.IsNullOrWhiteSpace(cfg["LegacyMes:FileRoot"]) ? @"\\10.10.40.98\nas\00.MESServer" : cfg["LegacyMes:FileRoot"]!.Trim();
        _certDirs = Split(cfg["LegacyMes:CertDirs"], "Inspection_PDF,Inspection");
        _templateDirs = Split(cfg["LegacyMes:CertTemplateDirs"], "Inspection_cov,Inspection");
    }

    private static string[] Split(string? s, string def) =>
        (string.IsNullOrWhiteSpace(s) ? def : s).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsSafeId(string? id) => id is not null && SafeId.IsMatch(id) && !id.Contains("..");

    /// <summary>파일 이름 하나(경로 없음)인지 — 성적서 파일명처럼 DB 에서 온 이름</summary>
    public static bool IsPlainFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n == Path.GetFileName(n) && !n.Contains("..") && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && n.IndexOfAny(['\\', '/', ':']) < 0;
    }

    private static bool Exists(string path)
    {
        try { return File.Exists(path); } catch (Exception) { return false; }
    }

    /// <summary>제품 사진 경로(없으면 null)</summary>
    public string? MatImage(string matId)
    {
        if (!IsSafeId(matId)) return null;
        return ImageExt.Select(e => Path.Combine(Root, "MatImage", matId + e)).FirstOrDefault(Exists);
    }

    public sealed record CertFile(string Name, string Folder, long Size, DateTime Modified, string FullPath);

    /// <summary>LOT 성적서 파일들 — 폴더 후보 순서대로, 같은 이름은 앞 폴더 것</summary>
    public List<CertFile> LotCerts(string lotId)
    {
        var list = new List<CertFile>();
        if (!IsSafeId(lotId)) return list;
        void Add(string path, string folder)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && !list.Any(x => x.Name.Equals(fi.Name, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new(fi.Name, folder, fi.Length, fi.LastWriteTime, fi.FullName));
            }
            catch (Exception) { /* NAS 가 안 닿으면 없음 */ }
        }
        foreach (var dir in _certDirs)
        {
            foreach (var e in CertExt) Add(Path.Combine(Root, dir, lotId + e), dir);
            var sub = Path.Combine(Root, dir, lotId);
            try
            {
                if (Directory.Exists(sub))
                    foreach (var f in Directory.EnumerateFiles(sub).Where(f => CertExt.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(50))
                        Add(f, $"{dir}\\{lotId}");
            }
            catch (Exception) { /* 없음 */ }
        }
        return list;
    }

    /// <summary>성적서 양식 경로(없으면 null)</summary>
    public string? Template(string? certFile)
    {
        if (!IsPlainFileName(certFile)) return null;
        var name = certFile!.Trim();
        return _templateDirs.Select(d => Path.Combine(Root, d, name)).FirstOrDefault(Exists);
    }

    /// <summary>찾아본 양식 폴더(못 찾았을 때 안내용)</summary>
    public IEnumerable<string> TemplateDirs => _templateDirs;

    public sealed record LotImage(string Path, string Folder, string Name, long Size, DateTime Modified, string FullPath);

    /// <summary>
    /// LOT 성적서 사진 — <c>Inspection_img\{LOT ID}\</c> 아래 사진 전부(예: <c>4.Cleaning Status\1ck_Cleaning_Before_Image.png</c>).
    /// Path 는 LOT 폴더 기준 상대 경로(받을 때 이 목록에 있는 것만 내준다).
    /// </summary>
    public List<LotImage> LotImages(string lotId)
    {
        var list = new List<LotImage>();
        if (!IsSafeId(lotId)) return list;
        var dir = System.IO.Path.Combine(Root, "Inspection_img", lotId);
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                         .Where(f => ImageExt.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).Take(300))
            {
                var fi = new FileInfo(f);
                var rel = System.IO.Path.GetRelativePath(dir, fi.FullName);
                var folder = System.IO.Path.GetDirectoryName(rel) ?? "";
                list.Add(new(rel.Replace('/', '\\'), folder.Replace('/', '\\'), fi.Name, fi.Length, fi.LastWriteTime, fi.FullName));
            }
        }
        catch (Exception) { /* NAS 가 안 닿으면 없음 */ }
        return list.OrderBy(x => x.Folder, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
