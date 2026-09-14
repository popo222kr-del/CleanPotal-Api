using System.Security.Cryptography;
using System.Text;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Security;
using CleanPotal.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleanPotal.Tests;

public class AuthServiceTests
{
    // 테스트 전용 서명 키 — 운영 키가 아니며 토큰 발급이 되는지만 확인한다.
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-only-signing-key-at-least-32-bytes-long!",
            ["Jwt:Issuer"] = "CleanPotal.Api",
            ["Jwt:Audience"] = "CleanPotal.Clients",
            ["Jwt:ExpiryHours"] = "12",
        })
        .Build();

    private static User NewUser(string username, string hash) => new()
    {
        Username = username,
        PasswordHash = hash,
        RealName = "박주언",
        TeamName = "김팀",
    };

    [Fact]
    public async Task 올바른_비밀번호면_토큰이_발급된다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("1004", PasswordHasher.Hash("1234")));
        await t.Db.SaveChangesAsync();

        var svc = new AuthService(t.Db, Config());
        var res = await svc.LoginAsync(new LoginRequest("1004", "1234"));

        Assert.NotNull(res);
        Assert.False(string.IsNullOrWhiteSpace(res!.Token));
        Assert.Equal("1004", res.User.Username);
    }

    [Fact]
    public async Task 비밀번호가_틀리거나_없는_계정이면_null()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("1004", PasswordHasher.Hash("1234")));
        await t.Db.SaveChangesAsync();

        var svc = new AuthService(t.Db, Config());
        Assert.Null(await svc.LoginAsync(new LoginRequest("1004", "wrong")));
        Assert.Null(await svc.LoginAsync(new LoginRequest("없는계정", "1234")));
    }

    [Fact]
    public async Task 퇴사자는_비밀번호가_맞아도_로그인되지_않는다()
    {
        using var t = new TestDb();
        var u = NewUser("9999", PasswordHasher.Hash("1234"));
        u.IsResigned = true;
        t.Db.Users.Add(u);
        await t.Db.SaveChangesAsync();

        var svc = new AuthService(t.Db, Config());
        Assert.Null(await svc.LoginAsync(new LoginRequest("9999", "1234")));
    }

    [Fact]
    public async Task WPF_구형_해시로_로그인하면_BCrypt_로_재해시된다()
    {
        using var t = new TestDb();
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1234")));
        t.Db.Users.Add(NewUser("1004", legacy));
        await t.Db.SaveChangesAsync();

        var svc = new AuthService(t.Db, Config());
        Assert.NotNull(await svc.LoginAsync(new LoginRequest("1004", "1234")));

        using var fresh = t.NewContext();
        var saved = fresh.Users.Single(x => x.Username == "1004").PasswordHash;
        Assert.StartsWith("$2", saved);                       // 약한 해시가 DB에 남지 않는다
        Assert.True(PasswordHasher.Verify("1234", saved));    // 같은 비밀번호로 계속 로그인된다
    }

    [Fact]
    public async Task 비밀번호를_바꾸면_지문이_바뀌어_기존_토큰이_무효가_된다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("1004", PasswordHasher.Hash("old-pw")));
        await t.Db.SaveChangesAsync();

        var user = t.Db.Users.Single();
        var before = PasswordHasher.Fingerprint(user.PasswordHash);

        var svc = new AuthService(t.Db, Config());
        var (ok, error, res) = await svc.ChangeCredentialsAsync(
            user.Id, new ChangeCredentialsRequest("old-pw", null, "new-pw"));

        Assert.True(ok, error);
        Assert.NotNull(res);

        using var fresh = t.NewContext();
        var after = PasswordHasher.Fingerprint(fresh.Users.Single().PasswordHash);
        Assert.NotEqual(before, after);   // 옛 토큰의 pwv 는 더 이상 일치하지 않는다
    }

    [Fact]
    public async Task 현재_비밀번호가_틀리면_변경되지_않는다()
    {
        using var t = new TestDb();
        t.Db.Users.Add(NewUser("1004", PasswordHasher.Hash("old-pw")));
        await t.Db.SaveChangesAsync();
        var id = t.Db.Users.Single().Id;

        var svc = new AuthService(t.Db, Config());
        var (ok, _, _) = await svc.ChangeCredentialsAsync(
            id, new ChangeCredentialsRequest("틀린비번", null, "new-pw"));

        Assert.False(ok);
        using var fresh = t.NewContext();
        Assert.True(PasswordHasher.Verify("old-pw", fresh.Users.Single().PasswordHash));
    }
}
