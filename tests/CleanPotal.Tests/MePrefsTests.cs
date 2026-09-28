using System.Security.Claims;
using System.Text.Json;
using CleanPotal.Api.Controllers;
using CleanPotal.Core.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>내 화면 설정(달력 부서·교대 표시 등)을 계정에 저장 — PC·폰이 같게 보인다.</summary>
public class MePrefsTests
{
    private static MePrefsController Ctl(TestDb t, int uid) => new(t.Db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", uid.ToString()) }, "test")),
            },
        },
    };

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private static async Task<string> GetJson(MePrefsController c)
        => Assert.IsType<ContentResult>(await c.Get()).Content!;

    [Fact]
    public async Task 설정은_이름별로_저장되고_다른_사람과_섞이지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.AddRange(new User { Id = 1, Username = "a", RealName = "가", PasswordHash = "x" },
                            new User { Id = 2, Username = "b", RealName = "나", PasswordHash = "x" });
        await t.Db.SaveChangesAsync();

        Assert.Equal("{}", await GetJson(Ctl(t, 1)));
        Assert.IsType<NoContentResult>(await Ctl(t, 1).Put("calendar", Json("""{"depts":[3],"shift":false}""")));
        Assert.IsType<NoContentResult>(await Ctl(t, 1).Put("other", Json("1")));

        using var doc = JsonDocument.Parse(await GetJson(Ctl(t, 1)));
        Assert.False(doc.RootElement.GetProperty("calendar").GetProperty("shift").GetBoolean());
        Assert.Equal(3, doc.RootElement.GetProperty("calendar").GetProperty("depts")[0].GetInt32());
        Assert.Equal("{}", await GetJson(Ctl(t, 2)));                       // 다른 사람은 그대로

        // null 을 보내면 그 설정만 지운다(화면 기본값으로)
        await Ctl(t, 1).Put("calendar", Json("null"));
        Assert.Equal("""{"other":1}""", await GetJson(Ctl(t, 1)));
    }

    [Fact]
    public async Task 이상한_이름이나_너무_큰_값은_받지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(new User { Id = 1, Username = "a", RealName = "가", PasswordHash = "x" });
        await t.Db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await Ctl(t, 1).Put("Bad Key!", Json("1")));
        Assert.IsType<BadRequestObjectResult>(await Ctl(t, 1).Put("calendar", Json($"\"{new string('x', 5000)}\"")));
        Assert.Equal("", t.Db.Users.AsNoTracking().Single().UiPrefs);
    }
}
