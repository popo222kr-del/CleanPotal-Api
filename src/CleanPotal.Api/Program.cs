using System.Text;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── DB: EF Core + SQLite (추후 PostgreSQL로 교체 가능) ──
// DB 파일은 기존과 동일하게 API 프로젝트 폴더(src\CleanPotal.Api\cleanpotal.db)에 둔다.
//  단, "실행 위치(CWD)와 무관하게" 항상 그 파일을 쓰도록 절대경로로 고정한다.
//  (예전엔 상대경로 "Data Source=cleanpotal.db"가 CWD 기준이라, 백엔드를
//   다른 폴더에서 켜면 엉뚱한 곳에 빈 DB가 생겨 데이터가 초기화된 것처럼 보였음)
// AppContext.BaseDirectory(빌드 산출물)에서 .csproj가 있는 상위=프로젝트 폴더를 찾는다.
static string FindApiProjectDir(string startDir, string fallback)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null && Directory.GetFiles(dir.FullName, "*.csproj").Length == 0)
        dir = dir.Parent;
    return dir?.FullName ?? fallback;
}
var projectDir = FindApiProjectDir(AppContext.BaseDirectory, builder.Environment.ContentRootPath);
var dbFile = Path.Combine(projectDir, "cleanpotal.db");
// 설정에 '절대경로' 연결 문자열이 지정된 경우만 존중하고, 그 외에는 위 고정 경로 사용
var cfgConn = builder.Configuration.GetConnectionString("Default");
var conn = !string.IsNullOrWhiteSpace(cfgConn) ? cfgConn! : $"Data Source={dbFile}";
Console.WriteLine($"[db] SQLite 파일(고정): {dbFile}");
builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt.UseSqlite(conn));

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
builder.Services.AddAuthorization(opt =>
{
    // 파일 관리 권한: admin 역할이거나 perm=files 클레임 보유
    opt.AddPolicy("CanManageFiles", p => p.RequireAssertion(ctx =>
        ctx.User.IsInRole("admin") ||
        ctx.User.HasClaim("perm", "files")));

    // 일정/교육 관리 권한: admin 역할이거나 perm=schedule 클레임 보유
    opt.AddPolicy("CanManageSchedule", p => p.RequireAssertion(ctx =>
        ctx.User.IsInRole("admin") ||
        ctx.User.HasClaim("perm", "schedule")));

    // 업체 관리 권한: admin 역할이거나 perm=vendors 클레임 보유
    opt.AddPolicy("CanManageVendors", p => p.RequireAssertion(ctx =>
        ctx.User.IsInRole("admin") ||
        ctx.User.HasClaim("perm", "vendors")));
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

    db.Database.Migrate();
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
app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
