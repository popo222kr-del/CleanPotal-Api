using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 사용자 관리 API 는 <b>권한을 나눠 주는 자리</b>다. 여기가 열리면 누구나 스스로를 관리자로 만들 수
/// 있어 다른 모든 권한이 뜻을 잃는다. 실제 요청으로 확인한다.
///
/// 컨트롤러에 정책이 붙어 있는지는 눈으로 볼 수 있지만, 그 정책이 실제 요청에서 막아 주는지는
/// 띄워 보지 않으면 알 수 없다 — 등급을 아무리 올려도 관리자 전용은 열리지 않아야 한다.
/// </summary>
[Collection(PortalAppCollection.Name)]
public class AdminEndpointAuthTests
{
    private readonly PortalAppFixture _app;
    public AdminEndpointAuthTests(PortalAppFixture app) => _app = app;

    [Fact]
    public async Task 토큰이_없으면_사용자_목록은_401()
    {
        var client = _app.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task 일반_사용자는_사용자_목록을_볼_수_없다()
    {
        // MES 편집까지 받은 사람이다 — 업무 등급이 높아도 관리자 전용은 열리지 않는다.
        var client = await _app.SignInAsync("mes-edit");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task 일반_사용자는_남의_권한을_바꿀_수_없다()
    {
        var client = await _app.SignInAsync("mes-view");
        var response = await client.PostAsJsonAsync("/api/users/perms",
            new { changes = new[] { new { id = 1, key = "mes", value = 2 } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 일반_사용자는_계정을_만들_수_없다()
    {
        var client = await _app.SignInAsync("field-only");
        var response = await client.PostAsJsonAsync("/api/users", new
        {
            username = "몰래만든계정", password = "pw1234", realName = "몰래", department = "", teamName = "",
            rank = "", jobTitle = "", email = "", phoneNumber = "", employeeNumber = "",
            hireDate = "", isResigned = false, resignDate = "", isAdmin = true,
            accessSchedule = 2, accessRoster = 2, accessHandover = 2, accessField = 2, accessOffice = 2,
            accessMes = 2, mesPermissions = (string?)null, hiddenMenus = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 관리자는_사용자_목록을_볼_수_있다()
    {
        var client = await _app.SignInAsync("admin");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
    }
}
