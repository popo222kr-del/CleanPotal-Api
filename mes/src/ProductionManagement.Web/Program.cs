using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Infrastructure;
using ProductionManagement.Infrastructure.Data;
using ProductionManagement.Web.Components;
using ProductionManagement.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// CleanPotal 저장소 안의 App_Data를 MES의 단일 데이터 루트로 사용한다.
// 실행 위치가 달라도 DB/첨부파일 경로가 바뀌지 않도록 절대 경로로 정규화한다.
var mesDataRootSetting = builder.Configuration["MesData:RootPath"] ?? "App_Data";
var mesDataRoot = Path.IsPathRooted(mesDataRootSetting)
    ? mesDataRootSetting
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, mesDataRootSetting));
Directory.CreateDirectory(mesDataRoot);
Directory.CreateDirectory(Path.Combine(mesDataRoot, "Documents"));
builder.Configuration["ConnectionStrings:ProductionManagementDb"] =
    $"Data Source={Path.Combine(mesDataRoot, "Production.db")};Default Timeout=30";
builder.Configuration["Documents:RootPath"] = Path.Combine(mesDataRoot, "Documents");

// ── Blazor(대화형 서버) ───────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);

// ── 프록시(포털) 뒤에서 원래 요청 주소를 되찾는다 ─────────────────────────────────
// MES 는 브라우저가 직접 부르지 않는다. 개발은 Vite, 운영은 CleanPotal.Api(YARP)가 앞에 선다.
// 이 설정이 없으면 ASP.NET Core 가 절대 URL(로그인 리다이렉트 Location 등)을 자기 주소인
// http://localhost:5206 으로 만들어 버려서, iframe 이 포털 origin 을 벗어나고 세션 쿠키가 끊긴다.
// 프록시는 항상 같은 PC(루프백)이므로 신뢰 대상도 루프백으로만 제한한다.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
    // 포털이 LAN IP/터널 도메인으로 열려도 그 Host 를 그대로 받아야 한다(사내망 전용).
    options.AllowedHosts.Clear();
});

// ── 인증(쿠키) + 인가 ─────────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".CleanPotal.MES";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        // 화면(iframe) 요청만 안내 페이지로 보낸다. fetch/Blazor 같은 비-화면 요청까지
        // 302 로 돌려보내면 React 쪽에서는 "성공(200 HTML)"으로 보여 원인을 알 수 없게 된다.
        // 이런 요청에는 401 을 주어 포털이 SSO 를 다시 태우도록 한다.
        options.Events.OnRedirectToLogin = context =>
        {
            if (WantsHtmlPage(context.Request))
            {
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (WantsHtmlPage(context.Request))
            {
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// 브라우저 주소창/iframe 이 문서를 가져가는 요청인지 판별한다.
// Sec-Fetch-Dest 를 보내지 않는 옛 브라우저를 위해 Accept 헤더도 함께 본다.
static bool WantsHtmlPage(HttpRequest request)
{
    var dest = request.Headers["Sec-Fetch-Dest"].ToString();
    if (!string.IsNullOrEmpty(dest))
        return dest is "document" or "iframe" or "frame";
    return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
// 정상 경로(프록시 경유)에서는 포털과 same-origin 이라 CORS 가 쓰이지 않는다.
// VITE_MES_URL 로 MES 를 직접 가리키는 예외 구성일 때만 쓰이므로, 허용 origin 은 설정에서 읽는다.
var portalOrigins = builder.Configuration
    .GetSection("Security:PortalOrigins")
    .GetChildren()
    .Select(item => item.Value)
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Select(value => value!.TrimEnd('/'))
    .ToArray();
builder.Services.AddCors(options => options.AddPolicy("CleanPotalPortal", policy =>
{
    if (portalOrigins.Length == 0)
    {
        // 허용 목록이 비면 same-origin(프록시 경유)만 동작한다 - 가장 안전한 기본값.
        policy.WithOrigins(Array.Empty<string>());
        return;
    }
    policy.WithOrigins(portalOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
// 포털 API 주소는 따로 설정하지 않는다. MES 는 항상 포털의 /mes-runtime 프록시를 통해서만
// 열리고, UseForwardedHeaders 가 원래 Scheme/Host 를 복원해 주므로 요청에서 그대로 얻을 수 있다.
// 그래서 개발·테스트·운영 어디에 올려도 고칠 설정이 없다.
// (예외적으로 주소를 고정해야 하면 Portal:ApiBaseUrl 로 덮어쓸 수 있다)
var portalApiOverride = builder.Configuration["Portal:ApiBaseUrl"]?.TrimEnd('/');
builder.Services.AddHttpClient("CleanPotalApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ── 기존 계층 재사용: 비즈니스/데이터 계층을 그대로 조립한다(WPF와 동일한 서비스) ──────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

// ── 웹 전용 override ──────────────────────────────────────────────────────────
// (1) 현재 사용자: 데스크톱의 세션 싱글턴 대신 요청/서킷 단위(인증 상태 기반)로 교체(마지막 등록이 이김).
builder.Services.AddScoped<ICurrentUserProvider, BlazorCurrentUserProvider>();
builder.Services.AddScoped<IAuthorizationService, PortalAuthorizationService>();
// (2) 성적서 Excel COM 경계: 웹에서는 no-op(추후 서버 구현으로 대체). DI 그래프 성립용.
builder.Services.AddScoped<ICertificateExcelFiller, NoOpCertificateExcelFiller>();
// (3) 서킷 단위 DB 작업 줄 세우기(한 서킷이 DbContext 하나를 공유 - 겹친 이벤트로 EF 동시 작업 예외 방지).
builder.Services.AddScoped<ProductionManagement.Web.Services.DbWorkGate>();
// (4) LOT 바코드/QR: 사진 해독·QR 생성(상태 없음 → 싱글턴), 스캔 값 → LOT/현재 공정 조회.
builder.Services.AddSingleton<ProductionManagement.Web.Services.BarcodeService>();
builder.Services.AddScoped<ProductionManagement.Web.Services.LotScanResolver>();
builder.Services.AddScoped<ProductionManagement.Web.Services.BrowserFileService>();
builder.Services.AddScoped<ProductionManagement.Web.Services.LotListExcelExporter>();
builder.Services.AddScoped<IRunsheetGenerator, ProductionManagement.Web.Services.RunsheetExcelGenerator>();

var app = builder.Build();

// 프록시가 알려준 원래 Scheme/Host 를 가장 먼저 반영한다(UsePathBase 보다 앞).
// 이 줄이 없으면 아래 모든 절대 URL 생성이 MES 자기 주소(localhost:5206) 기준이 된다.
app.UseForwardedHeaders();

// 브라우저에는 CleanPotal과 동일한 origin의 /mes-runtime으로 노출하고,
// 실제 MES 프로세스는 localhost:5206에만 둔다(Vite/운영 프록시가 이 경로를 전달).
app.UsePathBase("/mes-runtime");

// MES는 로컬 CleanPotal 화면 안에서만 iframe으로 표시한다.
// 기본 SAMEORIGIN 헤더는 포트가 다른 로컬 앱도 차단하므로, 명시한 포털 origin만 허용한다.
var frameAncestors = string.Join(' ', builder.Configuration
    .GetSection("Security:FrameAncestors")
    .GetChildren()
    .Select(item => item.Value)
    .Where(value => !string.IsNullOrWhiteSpace(value)));
if (string.IsNullOrWhiteSpace(frameAncestors)) frameAncestors = "'self'";
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.Remove("X-Frame-Options");
        context.Response.Headers.ContentSecurityPolicy = $"frame-ancestors {frameAncestors}";
        return Task.CompletedTask;
    });
    await next();
});

// ── 앱 시작 시 DB 준비(WPF App.xaml.cs와 동일한 순서: Migrate → 선택적 개발 Seed) ────────────
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("MesData:SeedDevelopmentData"))
    {
        var lotNumberGenerator = sp.GetRequiredService<ILotNumberGenerator>();
        await DevelopmentDataSeeder.SeedAsync(db, lotNumberGenerator, new SystemCurrentUserProvider());
    }
}

// ── 파이프라인 ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// CleanPotal 로그인 JWT를 API에서 검증한 뒤 MES 쿠키 세션으로 교환한다.
// 포털 React 화면이 iframe을 표시하기 전에 이 엔드포인트를 한 번 호출하므로 MES 재로그인이 필요 없다.
app.MapGet("/auth/portal-session", async (HttpContext http, IHttpClientFactory clients) =>
{
    var authorization = http.Request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    // 요청이 들어온 포털 주소가 곧 토큰을 검증해 줄 API 다.
    var portalBase = string.IsNullOrWhiteSpace(portalApiOverride)
        ? $"{http.Request.Scheme}://{http.Request.Host}"
        : portalApiOverride;

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{portalBase}/api/auth/me");
    request.Headers.TryAddWithoutValidation("Authorization", authorization);
    using var response = await clients.CreateClient("CleanPotalApi").SendAsync(request);
    if (!response.IsSuccessStatusCode)
        return Results.Unauthorized();

    // 포털 API 는 모든 응답을 { success, data, error } 봉투로 감싼다(EnvelopeResultFilter).
    // 봉투째로 PortalUser 에 읽으면 최상위에 id/username 이 없어 값이 전부 비고,
    // Username 이 null 이 되어 토큰이 멀쩡해도 항상 401 로 떨어진다.
    var envelope = await response.Content.ReadFromJsonAsync<PortalEnvelope>();
    var portalUser = envelope?.Data;
    if (portalUser is null || portalUser.IsResigned || string.IsNullOrWhiteSpace(portalUser.Username))
        return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, portalUser.Id.ToString()),
        new(ClaimTypes.Name, portalUser.RealName),
        new(BlazorCurrentUserProvider.LoginIdClaimType, portalUser.Username),
        new("DisplayName", portalUser.RealName),
        new("Department", portalUser.Department ?? string.Empty),
        new("TeamName", portalUser.TeamName ?? string.Empty),
    };
    if (portalUser.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });

    return Results.NoContent();
})
.AllowAnonymous()
.RequireCors("CleanPotalPortal");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>포털 API 표준 응답 봉투 — 실제 값은 Data 안에 들어 있다.</summary>
internal sealed record PortalEnvelope(bool Success, PortalUser? Data, string? Error);

internal sealed record PortalUser(
    int Id,
    string Username,
    string RealName,
    string? Department,
    string? TeamName,
    bool IsResigned,
    bool IsAdmin);
