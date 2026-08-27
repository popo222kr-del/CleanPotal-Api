using System.Text;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── DB: EF Core (SQL Server 운영 / SQLite 는 레거시·마이그레이션 원본) ──
// 비밀번호가 든 연결 문자열은 git 에 올리지 않는다. 서버/로컬 각자의
// appsettings.local.json(선택) 에만 두고, 여기서 선택적으로 읽어들인다.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

// SQLite 파일 기본 경로: 실행 위치와 무관하게 API 프로젝트 폴더에 고정
// (레거시 SQLite 사용 시 + SQL Server 이전(migrate-to-sqlserver) 원본 기본값)
static string FindApiProjectDir(string startDir, string fallback)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null && Directory.GetFiles(dir.FullName, "*.csproj").Length == 0)
        dir = dir.Parent;
    return dir?.FullName ?? fallback;
}
var projectDir = FindApiProjectDir(AppContext.BaseDirectory, builder.Environment.ContentRootPath);
var defaultSqlitePath = Path.Combine(projectDir, "cleanpotal.db");

// 공급자 선택: 설정이 없으면 기존과 동일하게 SQLite(안전한 기본값 — 배포가
// 갑자기 깨지지 않게). SQL Server 로 전환하려면 appsettings.local.json 에
// "Database:Provider":"SqlServer" 와 ConnectionStrings:Default 를 넣는다.
var dbProvider = (builder.Configuration["Database:Provider"] ?? "Sqlite").Trim();
var cfgConn = builder.Configuration.GetConnectionString("Default");
var useSqlite = dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase);
if (useSqlite)
{
    var conn = !string.IsNullOrWhiteSpace(cfgConn) ? cfgConn! : $"Data Source={defaultSqlitePath}";
    Console.WriteLine($"[db] SQLite 사용: {conn}");
    builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt.UseSqlite(conn));
}
else
{
    if (string.IsNullOrWhiteSpace(cfgConn))
        Console.WriteLine("[db][경고] SQL Server 연결 문자열이 없습니다. appsettings.local.json 의 ConnectionStrings:Default 를 설정하세요.");
    Console.WriteLine("[db] SQL Server 사용");
    builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt.UseSqlServer(cfgConn));
}

// ── 비즈니스 서비스 계층 (DI) ──
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IScheduleBoardService, ScheduleBoardService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IHandoverService, HandoverService>();
builder.Services.AddScoped<IPortalService, PortalService>();
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
builder.Services.AddScoped<IRecipeService, RecipeService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INoticeService, NoticeService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IEducationService, EducationService>();
builder.Services.AddScoped<IWorkAssignmentService, WorkAssignmentService>();

// ── JWT 인증 ──
var jwt = builder.Configuration.GetSection("Jwt");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
        };
    });
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

var app = builder.Build();

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
        DbSeeder.Seed(db);                    // 기본 시드(계정이 이미 있으면 건드리지 않음)
        DataImporter.Run(db, folder);         // 최신 WPF 데이터 통째로 재적재(신규 직원만 추가)
        Console.WriteLine("[refresh] 완료. 웹을 새로고침하면 최신 WPF 데이터가 반영됩니다.");
        Console.WriteLine("[refresh] (계정·권한·부서·견적서 기준정보·설비목록·레시피는 보존됨 — 웹에서 관리)");
        return;
    }

    // 스키마 준비: SQL Server 는 모델에서 자동 생성(EnsureCreated),
    // SQLite 는 기존 손수 작성한 마이그레이션 적용(Migrate).
    if (useSqlite) db.Database.Migrate();
    else db.Database.EnsureCreated();
    DbSeeder.Seed(db);

    // 데이터 임포트 모드: `dotnet run -- import [폴더]`
    if (args.Length > 0 && args[0].Equals("import", StringComparison.OrdinalIgnoreCase))
    {
        var folder = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "import");
        DataImporter.Run(db, Path.GetFullPath(folder));
        return;   // 임포트 후 서버 시작 없이 종료
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseMiddleware<CleanPotal.Api.Infrastructure.ExceptionMiddleware>();

// 프론트(React 빌드 결과물)를 wwwroot에서 직접 서빙 — 단일 사이트/단일 포트 배포
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// 컨트롤러에 매칭 안 되는 나머지 경로는 index.html로 돌려 React Router가 처리하게 함
app.MapFallbackToFile("index.html");

app.Run();
