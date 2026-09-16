using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 포털을 실제로 띄워 두는 자리(테스트 한 벌에 한 번).
///
/// 설정은 <b>환경변수로</b> 넣는다. 이 앱은 WebApplication.CreateBuilder 단계에서 DB 공급자·연결
/// 문자열을 곧바로 읽어 쓰므로, 호스트를 만든 뒤에 얹는 방식(ConfigureAppConfiguration)으로는
/// 이미 늦다 — 그렇게 넣으면 테스트가 임시 DB 가 아니라 개발용 기본 파일을 쓰게 된다.
/// </summary>
public sealed class PortalAppFixture : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"portal-http-{Guid.NewGuid():N}.db");
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"portal-http-data-{Guid.NewGuid():N}");

    /// <summary>계정 이름이 겹치지 않게 — 임시 DB 라 해도 같은 이름이 둘이면 로그인이 흔들린다.</summary>
    public string Suffix { get; } = Guid.NewGuid().ToString("N")[..8];

    public const string Password = "pw1234";
    public WebApplicationFactory<Program> Factory { get; }

    public PortalAppFixture()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-signing-key-32-bytes-or-more");
        Environment.SetEnvironmentVariable("MesData__RootPath", _dataRoot);

        Factory = new WebApplicationFactory<Program>();

        // Services 를 건드리는 순간 호스트가 뜬다(스키마·기준 데이터까지).
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CleanPotalDbContext>();
        db.Users.AddRange(
            NewUser($"mes-none-{Suffix}", accessMes: 0),
            NewUser($"mes-view-{Suffix}", accessMes: 1),
            NewUser($"mes-edit-{Suffix}", accessMes: 2),
            FieldOnly($"field-only-{Suffix}"),
            Admin($"admin-{Suffix}"));
        db.SaveChanges();
    }

    /// <summary>그 계정으로 로그인한 HttpClient. 로그인 자체가 막히면 뒤의 판정이 뜻을 잃으므로 여기서 먼저 드러낸다.</summary>
    public async Task<HttpClient> SignInAsync(string who)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = $"{who}-{Suffix}", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 표준 봉투 { success, data: { token, ... } }
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = payload.RootElement.GetProperty("data").GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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

    private static User Admin(string username)
    {
        var user = NewUser(username, accessMes: 0);
        user.IsAdmin = true;
        return user;
    }

    private static User FieldOnly(string username)
    {
        var user = NewUser(username, accessMes: 0);
        user.AccessField = 2;
        user.AccessHandover = 2;
        return user;
    }

    public void Dispose()
    {
        Factory.Dispose();
        // 뒷정리는 실패해도 테스트 결과를 바꾸지 않는다(윈도우에서는 SQLite 가 파일을 잡고 있을 수 있다).
        try { File.Delete(_dbPath); } catch (Exception) { /* 임시 파일은 남아도 된다 */ }
        try { Directory.Delete(_dataRoot, recursive: true); } catch (Exception) { /* 위와 같다 */ }
    }
}

/// <summary>
/// 실제 HTTP 요청으로 401/403 이 나오는지.
///
/// 정책이 컨트롤러에 붙어 있는지(<c>MesEndpointPolicyTests</c>)와 그 정책이 무엇을 통과시키는지
/// (<c>DbPermissionHandlerTests</c>)는 따로 확인하고 있지만, 둘을 이어 붙인 실제 요청은 아무도
/// 확인하지 않았다. 인증·정책 배선이 하나만 어긋나도 전부 열리거나 전부 막히는데, 그때 두 단위
/// 테스트는 그대로 통과한다.
/// </summary>
/// <summary>포털을 한 번만 띄워 여러 테스트가 같이 쓴다 — 호스트를 반복해 띄우면 그만큼 느려진다.</summary>
[CollectionDefinition(Name)]
public sealed class PortalAppCollection : ICollectionFixture<PortalAppFixture>
{
    public const string Name = "포털 호스트";
}

[Collection(PortalAppCollection.Name)]
public class MesEndpointAuthTests
{
    private readonly PortalAppFixture _app;
    public MesEndpointAuthTests(PortalAppFixture app) => _app = app;

    private Task<HttpClient> SignInAsync(string who) => _app.SignInAsync(who);

    [Fact]
    public async Task 토큰이_없으면_401()
    {
        var client = _app.Factory.CreateClient();
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

        // 자료가 없어 업무 규칙에서 막힐 수는 있어도 권한으로 막히면 안 된다.
        var write = await client.PostAsJsonAsync("/api/mes/batch", new { lotIds = new[] { 1, 2 } });
        Assert.NotEqual(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task 다른_영역_권한으로는_MES_가_열리지_않는다()
    {
        var client = await SignInAsync("field-only");
        var response = await client.GetAsync("/api/mes/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
