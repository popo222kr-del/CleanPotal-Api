using System.Net;
using System.Text.Json;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CleanPotal.Api.Infrastructure;

/// <summary>
/// 외부 접속 보안 설정(관리자 › 외부 접속 보안). CheckSettings 키-값 표에 'site:security' 로 JSON 한 덩어리.
/// </summary>
/// <param name="Enforce">외부 접속 제한을 켰는가. 끄면 누구나 어디서든(지금까지처럼) — 접속 기록만 남긴다.</param>
/// <param name="InternalRanges">사내로 볼 IP 대역(CIDR 또는 IP 하나). 회사 와이파이·유선 대역, 필요하면 회사 공인 IP.</param>
/// <param name="ExternalHidden">사외에서는 열지 않는 메뉴 경로(관리자 영역은 이 목록과 상관없이 늘 사내 전용).</param>
public sealed record SecurityConfig(bool Enforce, IReadOnlyList<string> InternalRanges, IReadOnlyList<string> ExternalHidden)
{
    /// <summary>처음 값 — 사설 IP 대역 전부(사내망), 견적·단가와 Daily 업무 보고(출하 금액)는 사외 차단. 제한은 꺼 둔다.</summary>
    public static readonly SecurityConfig Default = new(false,
        new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" },
        new[] { "/quotation", "/work/report" });
}

/// <summary>
/// 사내·사외 판단과 외부 접속 보안 설정 읽기. 설정은 30초 동안 메모리에 두고 쓴다(요청마다 DB 를 보지 않게).
///
/// 사내/사외는 접속한 IP(HttpContext.Connection.RemoteIpAddress)로만 가른다. X-Forwarded-For 같은 머리글은
/// 밖에서 아무 값이나 넣을 수 있어 믿지 않는다(포털은 IIS 에 바로 붙어 있어 RemoteIpAddress 가 실제 접속 IP 다).
/// 모바일 데이터(LTE·5G)는 통신사 공인 IP 로 들어오므로 늘 사외, 회사 와이파이·유선은 사내 대역이면 사내.
/// </summary>
public class ExternalAccessPolicy
{
    public const string SettingKey = "site:security";
    private const string CacheKey = "security:config";
    /// <summary>요청 안에서 '사외라 제한받는 중' 표시 — 사외에서 막을 메뉴 목록(HashSet)이 들어간다.</summary>
    public const string ItemKey = "ext_hidden";

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    public ExternalAccessPolicy(IServiceScopeFactory scopes, IMemoryCache cache, IConfiguration config)
    {
        _scopes = scopes; _cache = cache; _config = config;
    }

    /// <summary>
    /// 비상 해제 — appsettings.local.json 에 "Security": { "DisableExternalLimit": true } 를 넣고 사이트를 다시 시작하면
    /// 화면 설정과 상관없이 제한이 꺼진다(잘못 켜서 관리자까지 못 들어올 때).
    /// </summary>
    public bool DisabledByConfig => _config.GetValue<bool>("Security:DisableExternalLimit");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SecurityConfig Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SecurityConfig.Default;
        try
        {
            var c = JsonSerializer.Deserialize<SecurityConfig>(raw, Json);
            if (c is null) return SecurityConfig.Default;
            return new SecurityConfig(c.Enforce,
                (c.InternalRanges ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList(),
                (c.ExternalHidden ?? Array.Empty<string>()).Select(x => x.Trim()).Where(x => x.StartsWith('/')).Distinct().ToList());
        }
        catch (JsonException) { return SecurityConfig.Default; }
    }

    public async Task<SecurityConfig> GetAsync()
    {
        if (_cache.TryGetValue(CacheKey, out SecurityConfig? hit) && hit is not null) return hit;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
        var raw = (await db.CheckSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SettingKey))?.Value;
        var cfg = Parse(raw);
        if (DisabledByConfig) cfg = cfg with { Enforce = false };
        _cache.Set(CacheKey, cfg, TimeSpan.FromSeconds(30));
        return cfg;
    }

    public async Task SaveAsync(CleanPotalDbContext db, SecurityConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, Json);
        var row = await db.CheckSettings.FirstOrDefaultAsync(s => s.Key == SettingKey);
        if (row is null) db.CheckSettings.Add(new CheckSetting { Key = SettingKey, Value = json });
        else row.Value = json;
        await db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }

    /// <summary>
    /// 사내 IP 인가 — 이 PC 자신(127.0.0.1·::1)은 늘 사내. 대역은 'a.b.c.d/n'(CIDR) 또는 IP 하나.
    /// 잘못 적힌 대역은 건너뛴다(설정 화면이 저장 전에 걸러 준다).
    /// </summary>
    public static bool IsInternal(IPAddress? ip, IEnumerable<string> ranges)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        foreach (var r in ranges)
            if (TryParseRange(r, out var net, out var bits) && InRange(ip, net, bits)) return true;
        return false;
    }

    public static bool TryParseRange(string text, out IPAddress net, out int bits)
    {
        net = IPAddress.None; bits = 0;
        var t = (text ?? "").Trim();
        var slash = t.IndexOf('/');
        var addr = slash < 0 ? t : t[..slash];
        // IPAddress.TryParse 는 '10.0.0' 같은 줄인 표기도 받아 주지만(10.0.0.0), 잘못 적은 것일 가능성이 커 받지 않는다.
        if (!addr.Contains(':') && addr.Split('.').Length != 4) return false;
        if (!IPAddress.TryParse(addr, out var a) || a is null) return false;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        var max = a.GetAddressBytes().Length * 8;
        if (slash < 0) bits = max;
        else if (!int.TryParse(t[(slash + 1)..], out bits) || bits < 0 || bits > max) return false;
        net = a;
        return true;
    }

    private static bool InRange(IPAddress ip, IPAddress net, int bits)
    {
        var a = ip.GetAddressBytes();
        var b = net.GetAddressBytes();
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }

    /// <summary>
    /// 사외 제한을 받는 중이면 화면용 사용자 정보에 표시를 단다 — 숨김 메뉴에 사외 차단 메뉴를 더하고 IsExternal 을 켠다.
    /// 관리자는 원래 숨김이 없으므로(화면이 무시한다) 사외 차단 메뉴만 쓴다.
    /// </summary>
    public static UserDto ForClient(UserDto u, IReadOnlyCollection<string>? extHidden)
    {
        if (extHidden is null) return u;
        var own = new List<string>();
        if (!u.IsAdmin)
            try { own = JsonSerializer.Deserialize<List<string>>(u.HiddenMenus) ?? new(); } catch (JsonException) { }
        var merged = own.Concat(extHidden).Distinct(StringComparer.Ordinal).ToList();
        return u with { HiddenMenus = JsonSerializer.Serialize(merged), IsExternal = true };
    }

    /// <summary>이 요청이 사외 제한을 받는 중인가(토큰 검증에서 정한다).</summary>
    public static HashSet<string>? Restricted(HttpContext? http) => http?.Items[ItemKey] as HashSet<string>;
}
