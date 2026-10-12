using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>메일 확인 — 웹메일 백업 ZIP 가져오기·읽기·지우기, 본인·사내 전용.</summary>
public class MailArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mail-test-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_root, true); } catch (Exception) { } }

    private static string Eml(string subject, string from, string to, string body, DateTime date, string? attachment = null, string? html = null)
    {
        var sb = new StringBuilder();
        sb.Append($"From: {from}\r\nTo: {to}\r\nSubject: =?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=\r\n");
        sb.Append($"Date: {date.ToUniversalTime():ddd, dd MMM yyyy HH:mm:ss} +0000\r\nMIME-Version: 1.0\r\n");
        if (attachment is null && html is null)
            return sb.Append($"Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(body))}\r\n").ToString();
        sb.Append("Content-Type: multipart/mixed; boundary=\"BB\"\r\n\r\n--BB\r\n");
        if (html is not null)
            sb.Append($"Content-Type: text/html; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}\r\n");
        else
            sb.Append($"Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(body))}\r\n");
        if (attachment is not null)
            sb.Append($"--BB\r\nContent-Type: text/plain; name=\"{attachment}\"\r\nContent-Disposition: attachment; filename=\"{attachment}\"\r\nContent-Transfer-Encoding: base64\r\n\r\n{Convert.ToBase64String(Encoding.UTF8.GetBytes("첨부 내용"))}\r\n");
        return sb.Append("--BB--\r\n").ToString();
    }

    private string Zip(string name, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, c) in files)
        {
            using var w = new StreamWriter(zip.CreateEntry(p).Open(), new UTF8Encoding(false));
            w.Write(c);
        }
        return path;
    }

    private (TestDb t, User me, MailArchiveService svc, MailArchiveStore store) Setup()
    {
        var t = new TestDb();
        var me = new User { Username = "1004", RealName = "나", Email = "me@aets.co.kr" };
        t.Db.Users.Add(me);
        t.Db.SaveChanges();
        var store = new MailArchiveStore(Path.Combine(_root, "store"));
        return (t, me, new MailArchiveService(t.Db, store), store);
    }

    [Fact]
    public async Task ZIP_을_가져오면_받은_보낸_메일함으로_나누고_같은_메일은_한_번만_넣는다()
    {
        var (t, me, svc, store) = Setup();
        using var _ = t;
        var d = new DateTime(2026, 9, 1, 9, 0, 0);
        var a = Eml("견적 문의", "고객 <cust@x.com>", "me@aets.co.kr", "단가 확인 부탁드립니다", d, "견적.txt");
        var b = Eml("회신 드립니다", "나 <me@aets.co.kr>", "cust@x.com", "확인했습니다", d.AddHours(1));
        var c = Eml("보낸 폴더 메일", "누구 <x@y.com>", "z@y.com", "폴더로 판단", d.AddHours(2));

        // 안에 ZIP 이 한 겹 더 있는 백업(메일함별 묶음)
        var inner = Zip("inner.zip", ("c.eml", c));
        var outer = Path.Combine(_root, "backup.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            void Put(string p, string content) { using var w = new StreamWriter(zip.CreateEntry(p).Open()); w.Write(content); }
            Put("받은메일함/a.eml", a);
            Put("기타/b.eml", b);
            Put("받은메일함/a-dup.eml", a);   // 같은 메일 두 번
            Put("readme.txt", "무시");
            zip.CreateEntryFromFile(inner, "보낸메일함.zip");
        }

        var r = await svc.ImportAsync(me, outer, "backup.zip");
        Assert.Equal((4, 3, 1, 0), (r.Total, r.Added, r.Duplicates, r.Failed));

        var items = await t.Db.MailItems.OrderBy(m => m.Date).ToListAsync();
        Assert.Equal(new[] { "inbox", "sent", "sent" }, items.Select(m => m.Folder));   // b: 보낸 사람이 본인, c: 폴더 이름
        Assert.Equal("견적 문의", items[0].Subject);
        Assert.Equal("cust@x.com", items[0].FromAddr);
        Assert.Equal(1, items[0].AttachmentCount);
        Assert.Contains("단가 확인", items[0].Preview);
        Assert.All(items, m => Assert.True(File.Exists(store.PathOf(me.Id, m.Hash))));

        // 같은 ZIP 을 다시 올리면 전부 중복
        var again = await svc.ImportAsync(me, outer, "backup.zip");
        Assert.Equal((0, 4), (again.Added, again.Duplicates));
        Assert.Equal(3, await t.Db.MailItems.CountAsync());
    }

    [Fact]
    public async Task 메일이_없는_ZIP은_거절하고_기록도_남기지_않는다()
    {
        var (t, me, svc, _) = Setup();
        using var __ = t;
        var z = Zip("empty.zip", ("readme.txt", "메일 없음"));
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.ImportAsync(me, z, "empty.zip"));
        Assert.Equal(0, await t.Db.MailImports.CountAsync());
    }

    [Fact]
    public async Task 본문은_스크립트를_지우고_첨부와_원본을_돌려준다()
    {
        var (t, me, svc, _) = Setup();
        using var __ = t;
        var html = "<p onclick=\"x()\">안녕하세요</p><script>alert(1)</script><img src=\"http://track.example/p.gif\">";
        var z = Zip("one.zip", ("a.eml", Eml("본문", "a <a@b.com>", "me@aets.co.kr", "", DateTime.Now, "자료.txt", html)));
        await svc.ImportAsync(me, z, "one.zip");
        var item = await t.Db.MailItems.SingleAsync();

        var d = svc.Detail(item);
        Assert.Contains("안녕하세요", d.Html);
        Assert.DoesNotContain("<script", d.Html);
        Assert.DoesNotContain("onclick", d.Html);
        Assert.True(d.RemoteImagesBlocked);
        Assert.Equal("자료.txt", Assert.Single(d.Attachments).Name);

        var att = svc.Attachment(item, 0);
        Assert.Equal("첨부 내용", Encoding.UTF8.GetString(att!.Value.Bytes));
        Assert.Null(svc.Attachment(item, 5));
    }

    [Fact]
    public async Task 가져오기를_지우면_그때_들어온_메일과_원본이_지워진다()
    {
        var (t, me, svc, store) = Setup();
        using var __ = t;
        var r1 = await svc.ImportAsync(me, Zip("1.zip", ("a.eml", Eml("하나", "a@b.com", "c@d.com", "1", DateTime.Now))), "1.zip");
        await svc.ImportAsync(me, Zip("2.zip", ("b.eml", Eml("둘", "a@b.com", "c@d.com", "2", DateTime.Now))), "2.zip");
        var first = await t.Db.MailItems.SingleAsync(m => m.ImportId == r1.ImportId);

        Assert.Equal(1, await svc.DeleteImportAsync(me.Id, r1.ImportId));
        Assert.False(File.Exists(store.PathOf(me.Id, first.Hash)));
        Assert.Equal("둘", (await t.Db.MailItems.SingleAsync()).Subject);
        Assert.Equal(0, await svc.DeleteImportAsync(me.Id + 99, r1.ImportId));   // 남의 것은 지우지 못한다
    }

    [Fact]
    public async Task 본문_전체를_검색용으로_담고_예전_메일은_원본에서_채운다()
    {
        var (t, me, svc, store) = Setup();
        using var __ = t;
        var longBody = new string('가', 500) + " 맨끝낱말";
        await svc.ImportAsync(me, Zip("b.zip", ("a.eml", Eml("긴 본문", "a@b.com", "c@d.com", longBody, DateTime.Now))), "b.zip");
        var item = await t.Db.MailItems.SingleAsync();
        Assert.Contains("맨끝낱말", item.Body);          // 미리 보기(300자)에는 없는 뒷부분도 찾을 수 있다
        Assert.DoesNotContain("맨끝낱말", item.Preview);

        // 본문 칸이 생기기 전에 들어온 메일 흉내 → 서버가 뜰 때 원본에서 채운다
        item.Body = null;
        await t.Db.SaveChangesAsync();
        var services = new ServiceCollection();
        services.AddSingleton(t.Db);
        await MailArchiveService.BackfillBodiesAsync(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), store);
        t.Db.ChangeTracker.Clear();
        Assert.Contains("맨끝낱말", (await t.Db.MailItems.SingleAsync()).Body);
    }

    [Theory]
    [InlineData("보낸메일함/1.eml", "x@y.com", "inbox", "sent")]
    [InlineData("Sent/1.eml", "x@y.com", "inbox", "sent")]
    [InlineData("받은메일함/1.eml", "me@aets.co.kr", "sent", "inbox")]   // 폴더 이름이 먼저
    [InlineData("1.eml", "ME@aets.co.kr", "", "sent")]
    [InlineData("1.eml", "x@y.com", "", "inbox")]
    public void 메일함_판단(string path, string from, string _, string expected)
        => Assert.Equal(expected, MailArchiveService.FolderOf(path, from, "me@aets.co.kr"));

    // ── 본인·사내 전용 ──

    private static async Task<bool> GuardAsync(TestDb t, User user, string ip, string action = "List", bool internalOnly = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(t.Db);
        var sp = services.BuildServiceProvider();
        var ext = new ExternalAccessPolicy(sp.GetRequiredService<IServiceScopeFactory>(), new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().Build());
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("uid", user.Id.ToString()) }, "test")),
        };
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        var descriptor = new ControllerActionDescriptor { ActionName = action, EndpointMetadata = new List<object>() };
        var ctx = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var called = false;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["MailArchive:InternalOnly"] = internalOnly ? "true" : "false" }).Build();
        await new MailGuardFilter(t.Db, ext, config).OnActionExecutionAsync(ctx, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), new object()));
        });
        return called;
    }

    [Fact]
    public async Task 처음에는_1004_만_쓸_수_있고_사외에서도_열린다()
    {
        using var t = new TestDb();
        var admin = new User { Username = "1004", RealName = "관리", IsAdmin = true };
        var other = new User { Username = "kim", RealName = "김", IsAdmin = true };
        t.Db.Users.AddRange(admin, other);
        await t.Db.SaveChangesAsync();

        Assert.True(await GuardAsync(t, admin, "10.10.10.13"));
        Assert.True(await GuardAsync(t, admin, "211.234.10.20"));                                      // 휴대폰 데이터도 연다
        await Assert.ThrowsAsync<ForbiddenException>(() => GuardAsync(t, admin, "211.234.10.20", internalOnly: true));   // 사내 전용 설정이면 막는다
        await Assert.ThrowsAsync<ForbiddenException>(() => GuardAsync(t, other, "10.10.10.13"));      // 관리자라도 허용 목록 밖
        Assert.True(await GuardAsync(t, other, "211.234.10.20", action: "Access"));                    // 쓸 수 있는지 묻기는 통과

        // 허용 목록에 넣으면 쓸 수 있다
        t.Db.CheckSettings.Add(new CheckSetting { Key = MailAccess.SettingKey, Value = "[\"1004\",\"kim\"]" });
        await t.Db.SaveChangesAsync();
        Assert.True(await GuardAsync(t, other, "10.10.10.13"));
    }
}
