using System.Text;
using CleanPotal.Core.Interfaces;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ── DB: EF Core (SQL Server 운영 / SQLite 는 레거시·마이그레이션 원본) ──
// 비밀번호가 든 연결 문자열은 git 에 올리지 않는다. 서버/로컬 각자의
// appsettings.local.json(선택) 에만 두고, 여기서 선택적으로 읽어들인다.
// 환경변수는 파일보다 우선해야 IIS 설정과 통합 테스트가 로컬 개발 설정에 덮이지 않는다.
builder.Configuration
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// create-admin 에서 비밀번호를 화면에 표시하지 않고 입력받는다.
static string ReadHiddenLine()
{
    var sb = new StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
    return sb.ToString();
}

// SQLite 기본 파일은 앱의 콘텐츠 루트에 둔다. 개발 시에는 API 프로젝트 폴더,
// IIS 배포 시에는 사이트의 실제 publish 폴더다. 상위 폴더를 탐색하지 않아 IIS 계정이
// C:\ 같은 상위 경로를 열람할 권한이 없어도 앱이 정상적으로 시작된다.
var projectDir = builder.Environment.ContentRootPath;
var defaultSqlitePath = Path.Combine(builder.Environment.ContentRootPath, "cleanpotal.db");

// 공급자 선택: appsettings.local.json 의 "Database:Provider"("SqlServer" 또는 "Sqlite")와
// ConnectionStrings:Default 로 정한다. 설정 없이 SQLite 로 뜨는 것은 개발환경에서만 허용한다.
var dbProviderSetting = builder.Configuration["Database:Provider"];
var cfgConn = builder.Configuration.GetConnectionString("Default");
// 운영에서 설정이 빠진 채 뜨면 조용히 cleanpotal.db(SQLite)에 읽고 써서 SQL Server 와 데이터가 갈라진다.
// 개발환경이 아니면 공급자를 반드시 적게 하고, 없으면 시작하지 않는다(SQLite 를 쓰는 곳도 "Sqlite" 로 명시).
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(dbProviderSetting))
    throw new InvalidOperationException(
        "Database:Provider 설정이 없습니다. appsettings.local.json 에 \"Database\": { \"Provider\": \"SqlServer\" } (또는 \"Sqlite\") 를 넣으세요.");
var dbProvider = (dbProviderSetting ?? "Sqlite").Trim();
var useSqlite = dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
if (!useSqlite && string.IsNullOrWhiteSpace(cfgConn) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "SQL Server 연결 문자열(ConnectionStrings:Default)이 없습니다. appsettings.local.json 을 확인하세요.");
// 실제로 쓸 연결 문자열은 여기서 한 번만 정한다 — MES 도 같은 값을 받아 같은 DB 를 본다.
var effectiveConn = useSqlite
    ? (!string.IsNullOrWhiteSpace(cfgConn) ? cfgConn! : $"Data Source={defaultSqlitePath}")
    : (cfgConn ?? "");
if (useSqlite)
{
    Console.WriteLine($"[db] SQLite 사용: {effectiveConn}");
    builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt
        .UseSqlite(effectiveConn)
        // EF 9 부터 Migrate() 는 "모델이 마이그레이션보다 앞서 있다" 는 것을 경고가 아니라 예외로 다룬다.
        // 이 저장소에서 스키마를 실제로 맞추는 것은 마이그레이션이 아니라 SchemaUpgrader(없는 컬럼·테이블만
        // 덧붙임)이고, SQL Server 는 아예 마이그레이션을 쓰지 않는다. 그래서 모델에 컬럼을 하나 더할 때마다
        // 마이그레이션을 새로 만들지 않는데, 그대로 두면 SQLite 로 띄울 때 <b>서버가 시작되지 않는다</b>.
        // 여기서는 그 경고를 무시하고 넘어간 뒤 SchemaUpgrader 가 모자란 컬럼을 채운다.
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
}
else
{
    if (string.IsNullOrWhiteSpace(cfgConn))
        Console.WriteLine("[db][경고] SQL Server 연결 문자열이 없습니다. appsettings.local.json 의 ConnectionStrings:Default 를 설정하세요.");
    Console.WriteLine("[db] SQL Server 사용");
    builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt.UseSqlServer(effectiveConn));
}

// ── 비즈니스 서비스 계층 (DI) ──
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IScheduleBoardService, ScheduleBoardService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IHandoverService, HandoverService>();
builder.Services.AddScoped<IPortalService, PortalService>();
// 업무 파일 통합 관리의 파일 열기. 이 둘이 빠져 있어 PortalController 를 만들 수 없었고 /api/portal/* 가 모두 500 이었다.
builder.Services.AddScoped<IPortalFileService, PortalFileService>();
builder.Services.AddSingleton(_ => new CleanPotal.Api.Infrastructure.PortalLaunchTicketStore(TimeProvider.System));
builder.Services.AddSingleton<IHolidayService, HolidayService>();
builder.Services.AddScoped<IProdReqService, ProdReqService>();
builder.Services.AddScoped<IProductionMeetingService, ProductionMeetingService>();
builder.Services.AddScoped<IChecklistService, ChecklistService>();
builder.Services.AddScoped<IIcpmsService, IcpmsService>();
builder.Services.AddScoped<IBrokenService, BrokenService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<IQuotationMasterService, QuotationMasterService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INoticeService, NoticeService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IEducationService, EducationService>();
builder.Services.AddScoped<IWorkAssignmentService, WorkAssignmentService>();

// ── MES 첨부파일(성적서 등) 루트 ──
// LocalFileStorageService 는 Documents:RootPath 가 없으면 예외를 던진다 — 설정을 안 하면
// 성적서 화면이 열리는 순간 500 이 된다. 그래서 여기서 반드시 정해 준다.
//
// 이미 쓰던 파일이 MES 앱 폴더에 있으므로, 설정이 없으면 그쪽을 먼저 본다.
// 서버에서는 MesData__RootPath 로 공유폴더를 직접 지정하는 것이 확실하다(README 참고).
var mesDataSetting = builder.Configuration["MesData:RootPath"];
string mesDataRoot;
if (!string.IsNullOrWhiteSpace(mesDataSetting))
{
    mesDataRoot = Path.IsPathRooted(mesDataSetting)
        ? mesDataSetting
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, mesDataSetting));
}
else
{
    // 저장소 안의 MES 앱이 쓰던 폴더(있으면 그대로 이어 쓴다).
    var legacy = Path.GetFullPath(Path.Combine(
        projectDir, "..", "..", "mes", "src", "ProductionManagement.Web", "App_Data"));
    mesDataRoot = Directory.Exists(legacy) ? legacy : Path.Combine(projectDir, "App_Data");
}
Directory.CreateDirectory(Path.Combine(mesDataRoot, "Documents"));
builder.Configuration["Documents:RootPath"] = Path.Combine(mesDataRoot, "Documents");
Console.WriteLine($"[mes] 첨부파일 루트: {Path.Combine(mesDataRoot, "Documents")}");

// ── MES(ProductionManagement) 업무 계층 ──
// 화면만 React 로 새로 만들고, LOT 채번·공정 이동 규칙·이력 조회 같은 업무 로직은 MES 것을 그대로 쓴다.
// 자세한 이유와 갈아끼우는 부분은 Infrastructure/MesModule.cs 참고.
CleanPotal.Api.Infrastructure.MesModule.AddMes(builder.Services, effectiveConn, useSqlite);

// ── JWT 인증 ──
var jwt = builder.Configuration.GetSection("Jwt");

// 서명 키는 저장소에 두지 않는다. appsettings.local.json 의 Jwt:Key 또는 환경변수 Jwt__Key 로 주입.
// 운영환경에서 키가 없거나 과거에 커밋됐던 알려진 기본값이면 "조용히 취약하게" 뜨지 않고 즉시 실패시킨다.
const string KnownLeakedJwtKey = "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_KEY_IN_PRODUCTION_min32bytes!!";
// 개발 전용 고정 키 — 비밀이 아니며 개발환경에서만 사용된다(로컬 실행 편의).
const string DevOnlyJwtKey = "cleanpotal-local-development-only-signing-key-not-a-secret";
var jwtKey = jwt["Key"];
if (builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == KnownLeakedJwtKey)
    {
        jwtKey = DevOnlyJwtKey;
        Console.WriteLine("[auth] 개발환경: 임시 서명 키 사용 중(운영에서는 Jwt:Key 주입 필수).");
    }
}
else
{
    var problem =
        string.IsNullOrWhiteSpace(jwtKey) ? "설정되어 있지 않습니다"
        : jwtKey == KnownLeakedJwtKey ? "저장소에 공개됐던 기본값이라 사용할 수 없습니다"
        : Encoding.UTF8.GetByteCount(jwtKey) < 32 ? "너무 짧습니다(32바이트 이상 필요)"
        : null;
    if (problem is not null)
        throw new InvalidOperationException(
            $"[설정 오류] JWT 서명 키(Jwt:Key)가 {problem}. " +
            "배포 폴더의 appsettings.local.json 에 \"Jwt\": { \"Key\": \"<32바이트 이상 임의 문자열>\" } 를 추가하거나, " +
            "환경변수 Jwt__Key 를 설정한 뒤 다시 시작하세요. (키를 바꾸면 기존 로그인 토큰은 모두 무효가 되어 재로그인이 필요합니다.)");
}
// 검증·보정된 키를 설정에 되돌려 넣어, 토큰 발급(AuthService)과 검증(아래)이 항상 같은 키를 쓰게 한다.
builder.Configuration["Jwt:Key"] = jwtKey;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!)),
            // 만료 시각은 서버 시계 기준 그대로 적용(기본 5분 여유 제거)
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // 서명·만료가 유효해도 "지금 이 계정이 아직 유효한가"를 매 요청 확인한다.
        //  - 삭제된 계정 / 퇴사 처리된 계정 → 즉시 401
        //  - 비밀번호 변경 이후의 옛 토큰 → 즉시 401 (pwv 지문 불일치)
        // 여기서 읽은 사용자 정보를 HttpContext.Items 에 넣어 DbPermissionHandler 가 재사용하므로
        // 요청당 사용자 조회는 여전히 1회다.
        opt.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal;
                if (principal is null || !int.TryParse(principal.FindFirst("uid")?.Value, out var uid))
                {
                    ctx.Fail("세션 정보를 확인할 수 없습니다.");
                    return;
                }
                var db = ctx.HttpContext.RequestServices.GetRequiredService<CleanPotalDbContext>();
                var user = await db.Users.FindAsync(uid);
                if (user is null || user.IsResigned)
                {
                    ctx.Fail("사용할 수 없는 계정입니다.");
                    return;
                }
                var pwv = principal.FindFirst("pwv")?.Value;
                if (!string.Equals(pwv, PasswordHasher.Fingerprint(user.PasswordHash), StringComparison.Ordinal))
                {
                    // pwv 가 없는 토큰(이 기능 배포 전 발급분)도 여기서 걸러진다 → 한 번 재로그인하면 된다.
                    ctx.Fail("비밀번호가 변경되어 다시 로그인해야 합니다.");
                    return;
                }
                // 아이디(sub)가 바뀐 뒤의 옛 토큰도 막는다. MES 작업자 기록 등 sub 를 신원으로 쓰는 곳이 있어,
                // 그대로 두면 옛 아이디로 계속 기록된다. 기본 클레임 매핑이 켜져 있으면 sub 는 NameIdentifier 로 들어온다.
                var sub = principal.FindFirst("sub")?.Value
                    ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!string.Equals(sub, user.Username, StringComparison.Ordinal))
                {
                    ctx.Fail("아이디가 변경되어 다시 로그인해야 합니다.");
                    return;
                }
                ctx.HttpContext.Items["auth_user"] = user;
            },
        };
    });
// 현재 요청 사용자(작성자 본인 판정용) — 토큰 검증 때 읽어둔 User 를 재사용한다
builder.Services.AddScoped<ICurrentUser, CleanPotal.Api.Infrastructure.HttpCurrentUser>();
// 로그인 실패 횟수 제한(무차별 대입 완화) — 메모리 캐시 기반
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CleanPotal.Api.Infrastructure.LoginThrottle>();
// 권한 정책: 영역×등급, 전부 DB 기준(DbPermissionHandler) — 등급 변경 시 재로그인 없이 즉시 반영
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CleanPotal.Api.Infrastructure.DbPermissionHandler>();
builder.Services.AddAuthorization(opt =>
{
    void Acc(string policy, string area, int min) =>
        opt.AddPolicy(policy, p => p.AddRequirements(new CleanPotal.Api.Infrastructure.DbPermissionRequirement(area, min)));
    // 조회(1) / 편집(2) 정책 — 영역별
    Acc("ViewSchedule", "schedule", 1); Acc("EditSchedule", "schedule", 2);
    Acc("ViewRoster", "roster", 1); Acc("EditRoster", "roster", 2);
    Acc("ViewHandover", "handover", 1); Acc("EditHandover", "handover", 2);
    Acc("ViewField", "field", 1); Acc("EditField", "field", 2);
    Acc("ViewOffice", "office", 1); Acc("EditOffice", "office", 2);
    Acc("ViewReports", "reports", 1); Acc("EditReports", "reports", 2);   // 생산미팅(인수인계)∪주간보고(OFFICE)
    Acc("ViewVendors", "vendors", 1); Acc("EditVendors", "vendors", 2);   // 업체 관리 — OFFICE 메뉴 ∪ 기타세정 현황
    Acc("ViewMes", "mes", 1); Acc("EditMes", "mes", 2);                   // MES(생산관리)
    Acc("EditAttachment", "attach", 2);                                   // 첨부 올리기 — 어느 영역이든 편집 등급이면
    Acc("IsAdmin", "admin", 1);   // IsAdmin=true만 통과
});

// ── API ──
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<CleanPotal.Api.Infrastructure.EnvelopeResultFilter>();
});
builder.Services.AddEndpointsApiExplorer();

// Swagger + JWT 입력 버튼
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT 토큰을 입력하세요 (Bearer 접두사 없이 토큰만)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// ── CORS: 웹/모바일 클라이언트가 호출 ──
builder.Services.AddCors(o => o.AddPolicy("client", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── 현장 점검: Zigbee 온·습도 ──
// 포털이 Mosquitto(기본 127.0.0.1:1883)에 직접 붙어 센서 값을 받는다. 중간에 다른 프로그램을 두지 않는다.
// 구독은 백그라운드 서비스 안에서만 돌고, 브로커가 없거나 꺼져도 그 안에서 끝난다 —
// 온·습도는 곁다리 기능이라 이것 때문에 포털이 멈추면 안 된다.
builder.Services.Configure<CleanPotal.Core.Iot.ZigbeeOptions>(
    builder.Configuration.GetSection(CleanPotal.Core.Iot.ZigbeeOptions.SectionName));
builder.Services.AddSingleton<CleanPotal.Api.Controllers.AttachmentStore>();
builder.Services.AddSingleton<CleanPotal.Api.Infrastructure.ZigbeeSensorStore>();
builder.Services.AddHostedService<CleanPotal.Api.Infrastructure.ZigbeeMqttService>();
// 센서가 조용해도 그래프가 끊기지 않게, 마지막 값을 정해진 주기마다 이력에 적어 둔다.
builder.Services.AddHostedService<CleanPotal.Api.Infrastructure.ZigbeeSnapshotService>();

// ── MES(/mes-runtime) 리버스 프록시 ──
// MES(ProductionManagement.Web)는 별도 프로세스(기본 http://localhost:5206)로 뜨고,
// 브라우저에는 포털과 같은 origin 의 /mes-runtime 으로만 보인다. 그래야 iframe·쿠키·
// Blazor WebSocket 이 전부 same-origin 규칙 안에서 동작하고, MES 주소가 밖으로 새지 않는다.
//
// 경로는 자르지 않고 그대로 넘긴다 - MES 가 UsePathBase("/mes-runtime") 로 직접 떼어낸다.
// YARP 가 X-Forwarded-{Proto,Host} 를 붙여 주므로 MES 의 리다이렉트도 포털 주소로 나간다.
var mesRuntimeUrl = (builder.Configuration["Mes:RuntimeUrl"] ?? "http://localhost:5206").TrimEnd('/');
builder.Services.AddReverseProxy().LoadFromMemory(
    new[]
    {
        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "mes-runtime",
            ClusterId = "mes",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/mes-runtime/{**catch-all}" }
        },
        // "/mes-runtime"(끝 슬래시 없음) 단독 요청도 같은 클러스터로 보낸다.
        new Yarp.ReverseProxy.Configuration.RouteConfig
        {
            RouteId = "mes-runtime-root",
            ClusterId = "mes",
            Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/mes-runtime" }
        }
    },
    new[]
    {
        new Yarp.ReverseProxy.Configuration.ClusterConfig
        {
            ClusterId = "mes",
            Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
            {
                ["mes"] = new Yarp.ReverseProxy.Configuration.DestinationConfig { Address = mesRuntimeUrl }
            }
        }
    })
    // MES 가 토큰 확인에 쓸 포털 주소를 브라우저 Host 가 아닌 실제 수신 주소로 알려 준다(MesPortalEndpoint 참고).
    .AddTransforms(transforms => transforms.AddRequestTransform(ctx =>
    {
        ctx.ProxyRequest.Headers.Remove(CleanPotal.Api.Infrastructure.MesPortalEndpoint.HeaderName);
        var endpoint = CleanPotal.Api.Infrastructure.MesPortalEndpoint.From(ctx.HttpContext);
        if (endpoint is not null)
            ctx.ProxyRequest.Headers.TryAddWithoutValidation(CleanPotal.Api.Infrastructure.MesPortalEndpoint.HeaderName, endpoint);
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

// 기본 관리자(1004/1234) 자동 생성은 개발환경에서만 허용한다.
// 운영 최초 관리자는 `dotnet run -- create-admin <아이디>` 로 만든다(README 참고).
var isDev = app.Environment.IsDevelopment();

// 시작 시 마이그레이션 자동 적용 + 시드
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();

    // 스키마 확인 모드: `dotnet run -- schema "<경로>\dispatch.db"` (설치 없이 테이블 구조 출력)
    if (args.Length > 0 && args[0].Equals("schema", StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length > 1) DataImporter.DumpSchema(Path.GetFullPath(args[1]));
        else Console.WriteLine("[schema] 사용법: dotnet run -- schema \"<경로>\\dispatch.db\"");
        return;
    }

    // 운영 최초 관리자 생성:
    //   dotnet run -- create-admin <아이디>            (비밀번호는 화면에 안 보이게 입력받음)
    //   dotnet run -- create-admin <아이디> <비밀번호>  (자동화용 — 셸 기록에 남으니 주의)
    // 운영환경에는 기본 비밀번호 계정을 만들지 않으므로, 최초 1회 이 명령으로 관리자를 만든다.
    if (args.Length > 0 && args[0].Equals("create-admin", StringComparison.OrdinalIgnoreCase))
    {
        db.Database.EnsureCreated();
        if (args.Length < 2)
        {
            Console.WriteLine("[admin] 사용법: dotnet run -- create-admin <아이디> [비밀번호]");
            return;
        }
        var un = args[1].Trim();
        if (db.Users.Any(x => x.Username == un))
        {
            Console.WriteLine($"[admin] ❌ 이미 존재하는 아이디입니다: {un}");
            return;
        }
        string pw;
        if (args.Length > 2) pw = args[2];
        else
        {
            Console.Write("[admin] 새 비밀번호 입력(화면에 표시되지 않음): ");
            pw = ReadHiddenLine();
            Console.Write("[admin] 비밀번호 확인: ");
            if (ReadHiddenLine() != pw) { Console.WriteLine("[admin] ❌ 두 입력이 일치하지 않습니다."); return; }
        }
        if (pw.Length < 8)
        {
            Console.WriteLine("[admin] ❌ 비밀번호는 8자 이상이어야 합니다.");
            return;
        }
        db.Users.Add(new CleanPotal.Core.Entities.User
        {
            Username = un,
            PasswordHash = CleanPotal.Core.Security.PasswordHasher.Hash(pw),
            RealName = args.Length > 3 ? args[3] : un,
            TeamName = "Office", JobTitle = "관리자", EmployeeNumber = un,
            IsAdmin = true,
            AccessSchedule = 2, AccessRoster = 2, AccessHandover = 2, AccessField = 2, AccessOffice = 2, AccessMes = 2,
        });
        db.SaveChanges();
        Console.WriteLine($"[admin] ✅ 관리자 계정 생성 완료: {un} (비밀번호는 출력하지 않습니다)");
        return;
    }

    // 로그인 진단(읽기 전용, 아무것도 변경하지 않음):
    //   dotnet run -- check-login 1004
    //   dotnet run -- check-login 1004 "시도할비밀번호"
    // 저장된 해시의 "형식"만 출력하고 해시값·비밀번호는 절대 출력하지 않는다.
    if (args.Length > 0 && args[0].Equals("check-login", StringComparison.OrdinalIgnoreCase))
    {
        var un = args.Length > 1 ? args[1] : "1004";
        var u = db.Users.FirstOrDefault(x => x.Username == un);
        Console.WriteLine($"[check] 대상 계정: {un}");
        if (u is null)
        {
            Console.WriteLine($"[check] ❌ '{un}' 계정이 DB에 없습니다. (전체 계정 수: {db.Users.Count()}명)");
            var sample = db.Users.OrderBy(x => x.Id).Select(x => x.Username).Take(10).ToList();
            if (sample.Count > 0) Console.WriteLine($"[check]    존재하는 아이디 예시: {string.Join(", ", sample)}");
            return;
        }
        Console.WriteLine($"[check] 이름={u.RealName} / 관리자={u.IsAdmin} / 퇴사={u.IsResigned}");
        Console.WriteLine($"[check] 저장된 비밀번호 형식: {CleanPotal.Core.Security.PasswordHasher.Describe(u.PasswordHash)}");
        if (args.Length > 2)
        {
            var pw = args[2];
            var ok = CleanPotal.Core.Security.PasswordHasher.Verify(pw, u.PasswordHash);
            var scheme = CleanPotal.Core.Security.PasswordHasher.LegacyScheme(pw, u.PasswordHash);
            Console.WriteLine($"[check] 입력한 비밀번호 검증 결과: {(ok ? "✅ 성공" : "❌ 실패")}"
                              + (scheme is not null ? $" (구형 {scheme} 방식으로 일치)" : ""));
            if (!ok) Console.WriteLine("[check]    → 비밀번호가 다르거나, WPF가 쓰던 해시 방식이 아직 지원되지 않는 형식입니다.");
        }
        else Console.WriteLine("[check] (비밀번호까지 확인하려면: dotnet run -- check-login 1004 \"비밀번호\")");
        return;
    }

    // SQLite → SQL Server 일회성 데이터 이전:
    //   dotnet run -- migrate-to-sqlserver "C:\경로\cleanpotal.db"
    //   (연결은 appsettings.local.json 의 SQL Server 연결 문자열을 사용. 빈 대상 DB에서만.)
    if (args.Length > 0 && args[0].Equals("migrate-to-sqlserver", StringComparison.OrdinalIgnoreCase))
    {
        if (useSqlite)
        {
            Console.WriteLine("[migrate] 현재 공급자가 SQLite 입니다. appsettings.local.json 에서 SQL Server 로 설정 후 실행하세요.");
            return;
        }
        db.Database.EnsureCreated();
        var srcPath = args.Length > 1 ? Path.GetFullPath(args[1]) : defaultSqlitePath;
        SqlServerMigrator.CopyFromSqlite(db, srcPath);
        return;
    }

    // WPF → 웹 새로고침(전환 준비/전환일 반복 실행용):
    //   dotnet run -- refresh-from-wpf "C:\경로\WPF데이터폴더(dispatch.db 포함)"
    //   대상 SQL Server DB를 전부 비우고 → 기본 시드 → 최신 WPF 데이터를 통째로 다시 임포트한다.
    //   (WPF 를 계속 쓰는 병행 기간에 최신 데이터로 맞출 때, 그리고 전환일 최종 이관에 사용)
    if (args.Length > 0 && args[0].Equals("refresh-from-wpf", StringComparison.OrdinalIgnoreCase))
    {
        if (useSqlite)
        {
            Console.WriteLine("[refresh] 현재 공급자가 SQLite 입니다. appsettings.local.json 에서 SQL Server 로 설정 후 실행하세요.");
            return;
        }
        var folder = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "import");
        folder = Path.GetFullPath(folder);
        Console.WriteLine($"[refresh] WPF 데이터 폴더: {folder}");
        db.Database.EnsureCreated();          // 테이블 보장
        // 웹에서 관리하는 것(계정·비밀번호·권한, 부서/팀, 변경이력, 견적서 기준정보,
        // 스케줄보드 설비 목록·레시피 팔레트)은 비우지 않는다 → refresh 해도 초기화되지 않는다.
        SqlServerMigrator.ClearAllTables(db,
            typeof(CleanPotal.Core.Entities.User),
            typeof(CleanPotal.Core.Entities.OrgUnit),
            typeof(CleanPotal.Core.Entities.UserAuditLog),
            typeof(CleanPotal.Core.Entities.QuotationConfig),
            typeof(CleanPotal.Core.Entities.ScheduleEquipment),
            typeof(CleanPotal.Core.Entities.ScheduleRecipe));
        DbSeeder.Seed(db, isDev);             // 기본 시드(계정이 이미 있으면 건드리지 않음)
        DataImporter.Run(db, folder);         // 최신 WPF 데이터 통째로 재적재(신규 직원만 추가)
        Console.WriteLine("[refresh] 완료. 웹을 새로고침하면 최신 WPF 데이터가 반영됩니다.");
        Console.WriteLine("[refresh] (계정·권한·부서·견적서 기준정보·설비목록·레시피는 보존됨 — 웹에서 관리)");
        return;
    }

    // 스키마 재생성 + WPF 재적재 (일회성):
    //   dotnet run -- rebuild-from-wpf "C:\경로\WPF데이터폴더(dispatch.db 포함)"
    //   기존 테이블을 전부 DROP 하고 현재 모델대로 45개 테이블을 새로 만든 뒤 WPF 데이터를 적재한다.
    //   예전에 비정상 생성된 스키마([Content] 컬럼 등)를 바로잡고, As/In 컬럼명 변경도 반영한다.
    //   ⚠ 웹에서 설정한 계정·권한·설비 등도 함께 초기화되므로 전환 전 정비 단계에서만 사용.
    if (args.Length > 0 && args[0].Equals("rebuild-from-wpf", StringComparison.OrdinalIgnoreCase))
    {
        if (useSqlite)
        {
            Console.WriteLine("[rebuild] 현재 공급자가 SQLite 입니다. appsettings.local.json 에서 SQL Server 로 설정 후 실행하세요.");
            return;
        }
        var folder = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "import"));
        Console.WriteLine($"[rebuild] WPF 데이터 폴더: {folder}");
        SqlServerMigrator.DropAllTables(db);   // 잔재 스키마 제거
        db.Database.EnsureCreated();            // 현재 모델대로 테이블 새로 생성
        SchemaUpgrader.Run(db, useSqlite);
        DbSeeder.SeedBase(db);                  // 기본 시드(계정 제외 — 임포트가 실제 계정을 채우게)
        DataImporter.Run(db, folder);           // WPF 데이터 적재
        DbSeeder.SeedAdminFallback(db, isDev);  // 계정이 하나도 없을 때만(개발환경 한정) 최후 로그인 보장
        Console.WriteLine("[rebuild] 완료. 스키마를 새로 만들고 WPF 데이터를 적재했습니다([Content]→Content, As/In→As_ppb/In_ppb).");
        return;
    }

    // 과거 자료의 작성자를 이름 → 계정 ID 로 보정: `dotnet run -- backfill-authors`
    // 자동 실행하지 않는다(운영 데이터 변경이라 결과를 보고 판단해야 함).
    if (args.Length > 0 && args[0].Equals("backfill-authors", StringComparison.OrdinalIgnoreCase))
    {
        DatabaseSchemaInitializer.Prepare(db, useSqlite);
        AuthorBackfill.Run(db);
        return;
    }

    // 빈 DB는 현재 모델로 만들고, 운영 DB에는 없는 컬럼·테이블만 덧붙인다(추가 전용).
    // WPF에서 이어진 SQLite DB는 실제 스키마와 EF 마이그레이션 이력이 다를 수 있으므로
    // 시작 시 Migrate()를 실행하지 않는다. 기존 테이블을 다시 만들다 종료되는 것을 막는다.
    DatabaseSchemaInitializer.Prepare(db, useSqlite);
    DbSeeder.SeedBase(db);
    // MES 테이블(Mes 접두사)도 같은 DB 안에 만든다 — 없을 때만 만들고, 지우거나 바꾸지 않는다.
    CleanPotal.Api.Infrastructure.MesModule.EnsureSchema(scope.ServiceProvider);

    // 온·습도 센서 마스터: 설정에 적어 둔 센서 중 표에 없는 것만 심는다.
    // 이미 있는 줄은 건드리지 않는다 — 표에서 이름을 바꿔 두었을 수 있다.
    CleanPotal.Api.Infrastructure.ZigbeeSensorSeeder.Run(
        db, app.Services.GetRequiredService<IOptions<CleanPotal.Core.Iot.ZigbeeOptions>>().Value);

    // 영역 칸이 생기기 전에 올린 첨부에 영역을 채운다(기록에서 찾은 것만). 받을 때 그 화면 권한을 본다.
    AttachmentScopeBackfill.Run(db);

    // 데이터 임포트 모드: `dotnet run -- import [폴더]`
    if (args.Length > 0 && args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
    {
        var folder = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "import");
        DataImporter.Run(db, Path.GetFullPath(folder));
        DbSeeder.SeedAdminFallback(db, isDev);
        return;   // 임포트 후 서버 시작 없이 종료
    }
    DbSeeder.SeedAdminFallback(db, isDev);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseMiddleware<CleanPotal.Api.Infrastructure.ExceptionMiddleware>();

// 프론트(React 빌드 결과물)를 wwwroot에서 직접 서빙 — 단일 사이트/단일 포트 배포
// 캐시 규칙: 파일명에 해시가 붙는 /assets/* 는 1년 캐시, 나머지(index.html·sw.js 등)는 매번 서버에 확인.
// 이 설정이 없으면 브라우저가 옛 index.html 을 계속 써서 배포 뒤에도 예전 화면(옛 JS 번들)이 뜬다.
var spaStaticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(spaStaticFiles);

app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// MES 프록시는 SPA fallback 보다 먼저 매핑해야 한다. 뒤에 두면 /mes-runtime 이
// index.html 로 떨어져 iframe 안에 포털이 다시 열린다(MES 인증도 HTML 200 으로 오해됨).
app.MapReverseProxy();
// 없는 API 주소는 화면(index.html)이 아니라 404 로 답한다.
// 아래 fallback 이 /api 까지 삼키면, 화면은 HTML 을 JSON 으로 읽다가
// "Unexpected token '<'" 같은 엉뚱한 소리를 한다 — 서버가 옛 코드라 주소가 없는 것뿐인데도.
app.MapFallback("/api/{**rest}", (HttpContext ctx) =>
    Results.NotFound(new { error = $"없는 API 주소입니다: {ctx.Request.Path}" }));

// 컨트롤러에 매칭 안 되는 나머지 경로는 index.html로 돌려 React Router가 처리하게 함
app.MapFallbackToFile("index.html", spaStaticFiles);

app.Run();

// 통합 테스트(WebApplicationFactory)가 이 진입점을 잡을 수 있게 한다.
// 최상위 문(top-level statements)으로 만든 Program 은 internal 이라 테스트에서 보이지 않는다.
public partial class Program { }
