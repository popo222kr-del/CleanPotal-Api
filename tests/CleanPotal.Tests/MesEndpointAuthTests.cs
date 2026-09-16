using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 실제 HTTP 요청으로 401/403 이 나오는지.
///
/// 정책이 컨트롤러에 붙어 있는지(<c>MesEndpointPolicyTests</c>)와 그 정책이 무엇을 통과시키는지
/// (<c>DbPermissionHandlerTests</c>)는 따로 확인하고 있지만, 둘을 이어 붙인 실제 요청은 아무도
/// 확인하지 않았다. 인증·정책 배선이 하나만 어긋나도 전부 열리거나 전부 막히는데, 그때 두 단위
/// 테스트는 그대로 통과한다.
/// </summary>
public class MesEndpointAuthTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portal-http-{Guid.NewGuid():N}.db");
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"portal-http-data-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program> _factory = null!;

    private const string Password = "pw1234";

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // appsettings.local.json 이 있으면 그것이 나중에 얹히므로, 마지막에 다시 덮어쓴다.
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Jwt:Key"] = "integration-test-signing-key-32-bytes-or-more",
                ["MesData:RootPath"] = _dataRoot,
            }));
        });

        // 첫 요청에서 호스트가 뜨고 스키마·기준 데이터가 준비된다.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
        db.Users.AddRange(
            NewUser("mes-none", accessMes: 0),
            NewUser("mes-view", accessMes: 1),
            NewUser("mes-edit", accessMes: 2));
        db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { /* 임시 파일은 남아도 된다 */ }
        try { Directory.Delete(_dataRoot, recursive: true); } catch (IOException) { /* 위와 같다 */ }
        return Task.CompletedTask;
    }

    private static User NewUser(string username, int accessMes) => new()
    {
        Username = username,
        RealName = username,
        PasswordHash = PasswordHasher.Hash(Password),
        EmployeeNumber = username,
        IsAdmin = false,
        AccessSchedule = 0, AccessRoster = 0, AccessHandover = 0, AccessField = 0, AccessOffice = 0,
        AccessMes = accessMes,
    };

    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        response.EnsureSuccessStatusCode();

        // 표준 봉투 { success, data: { token, ... } }
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = payload.RootElement.GetProperty("data").GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task 토큰이_없으면_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/mes/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MES_등급이_0_이면_조회도_403()
    {
        var client = await SignInAsync("mes-none");
        var response = await client.GetAsync("/api/mes/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MES_조회_등급이면_조회는_되고_작업은_403()
    {
        var client = await SignInAsync("mes-view");

        var read = await client.GetAsync("/api/mes/dashboard");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // 묶기는 자료를 바꾸는 동작이라 편집 등급이 필요하다.
        var write = await client.PostAsJsonAsync("/api/mes/batch", new { lotIds = new[] { 1, 2 } });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task MES_편집_등급이면_작업_요청이_권한으로_막히지_않는다()
    {
        var client = await SignInAsync("mes-edit");

        // 자료가 없어 업무 규칙에서 막힐 수는 있어도(200 + success:false) 권한으로 막히면 안 된다.
        var write = await client.PostAsJsonAsync("/api/mes/batch", new { lotIds = new[] { 1, 2 } });
        Assert.NotEqual(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task 다른_영역_권한으로는_MES_가_열리지_않는다()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
            var user = NewUser("field-only", accessMes: 0);
            user.AccessField = 2;
            user.AccessHandover = 2;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = await SignInAsync("field-only");
        var response = await client.GetAsync("/api/mes/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
