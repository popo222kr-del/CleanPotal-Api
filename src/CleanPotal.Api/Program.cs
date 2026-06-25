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
var conn = builder.Configuration.GetConnectionString("Default") ?? "Data Source=cleanpotal.db";
builder.Services.AddDbContext<CleanPotalDbContext>(opt => opt.UseSqlite(conn));

// ── 비즈니스 서비스 계층 (DI) ──
builder.Services.AddScoped<IScheduleService, ScheduleService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IHandoverService, HandoverService>();
builder.Services.AddScoped<IPortalService, PortalService>();
builder.Services.AddSingleton<IHolidayService, HolidayService>();
builder.Services.AddScoped<IProdReqService, ProdReqService>();
builder.Services.AddScoped<IProductionMeetingService, ProductionMeetingService>();
builder.Services.AddScoped<IChecklistService, ChecklistService>();
builder.Services.AddScoped<IBrokenService, BrokenService>();
builder.Services.AddScoped<IQuotationService, QuotationService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();

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
});

// ── API ──
builder.Services.AddControllers();
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
    db.Database.Migrate();
    DbSeeder.Seed(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseCors("client");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
