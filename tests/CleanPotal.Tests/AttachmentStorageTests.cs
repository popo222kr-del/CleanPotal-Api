using System.Text;
using CleanPotal.Api.Controllers;
using CleanPotal.Api.Infrastructure;
using CleanPotal.Core.Entities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// 첨부 보관소: 탐색기로 봐도 알 수 있는 "분류\yyyy-MM\날짜_시각_이름" 저장, NAS 공유 경로 해석,
/// DB 칸 안의 base64 사진을 파일로 옮기기.
/// </summary>
public sealed class AttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"att-store-{Guid.NewGuid():N}");

    private AttachmentStore Store() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:AttachmentsPath"] = _root }).Build(),
        null!);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { /* 임시 폴더는 남아도 된다 */ }
    }

    [Fact]
    public async Task 분류_월_폴더에_날짜_시각_설명_이름으로_저장한다()
    {
        var store = Store();
        var when = new DateTime(2026, 9, 25, 14, 32, 5);
        using var src = new MemoryStream([1, 2, 3]);
        var a = await store.SaveAsync(src, "IMG_0001.jpg", "image/jpeg", 3, "kim", "체크시트", "주간_M-OUT 출고검사실_M-012_NG", when, default);

        Assert.Equal(Path.Combine("체크시트", "2026-09"), a.Folder);
        Assert.Equal("20260925_143205_주간_M-OUT 출고검사실_M-012_NG.jpg", a.StoredName);
        Assert.Equal("IMG_0001.jpg", a.FileName);   // 받을 때 이름은 올린 이름 그대로
        Assert.Equal("image", a.Kind);
        Assert.True(File.Exists(store.PathOf(a)));
    }

    [Fact]
    public async Task 같은_초_같은_이름이면_번호를_붙이고_덮어쓰지_않는다()
    {
        var store = Store();
        var when = new DateTime(2026, 9, 25, 8, 0, 0);
        using var s1 = new MemoryStream([1]);
        using var s2 = new MemoryStream([2]);
        var a = await store.SaveAsync(s1, "a.png", "image/png", 1, "kim", "BROKEN", null, when, default);
        var b = await store.SaveAsync(s2, "a.png", "image/png", 1, "kim", "BROKEN", null, when, default);

        Assert.Equal("20260925_080000_a.png", a.StoredName);
        Assert.Equal("20260925_080000_a_2.png", b.StoredName);
        Assert.Equal([1], File.ReadAllBytes(store.PathOf(a)));
        Assert.Equal([2], File.ReadAllBytes(store.PathOf(b)));
    }

    [Fact]
    public async Task 이름의_경로_문자는_지우고_폴더_밖으로_나가지_않는다()
    {
        var store = Store();
        using var src = new MemoryStream([1]);
        var a = await store.SaveAsync(src, "x.jpg", "image/jpeg", 1, "kim", "기타", @"..\..\windows\sys: *bad?", DateTime.Now, default);

        Assert.DoesNotContain('\\', a.StoredName);
        Assert.DoesNotContain('/', a.StoredName);
        Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(store.PathOf(a)));
    }

    [Theory]
    [InlineData("field", "체크시트", "체크시트")]
    [InlineData("field", null, "현장점검")]
    [InlineData("office", "BROKEN", "BROKEN")]
    [InlineData("reports", "..\\밖", "주간보고")]   // 목록에 없는 분류는 무시
    [InlineData("", null, "기타")]
    public void 분류는_목록에_있는_것만_쓴다(string scope, string? cat, string expected)
        => Assert.Equal(expected, AttachmentStore.CategoryOf(scope, cat));

    [Theory]
    [InlineData(@"\\10.10.40.98\천안공장\25. 생산 Inform 자료\주언\Clean_Data", @"\\10.10.40.98\천안공장")]
    [InlineData(@"\\nas\share", @"\\nas\share")]
    [InlineData(@"C:\Webjueon\publish\App_Data", null)]
    [InlineData(@"\\nas", null)]
    [InlineData("", null)]
    public void 공유폴더_경로에서_연결할_공유를_뽑는다(string path, string? expected)
        => Assert.Equal(expected, NetworkShare.ShareRoot(path));

    private static string DataUrl(byte[] bytes, string mime = "image/jpeg", string? name = null)
        => $"data:{mime}{(name is null ? "" : $";name={Uri.EscapeDataString(name)}")};base64,{Convert.ToBase64String(bytes)}";

    [Fact]
    public async Task DB_칸_안의_사진을_파일로_옮기고_칸에는_참조만_남긴다()
    {
        using var t = new TestDb();
        var store = Store();
        var img = DataUrl([0xFF, 0xD8, 0xFF, 0xD9]);
        var h = new Handover { Vendor = "A", Content = "c", Images = $"{{\"content\":[\"{img}\"],\"memo\":[\"att:9|x.png|image\"]}}", CreateDate = new DateTime(2025, 3, 2) };
        var p = new ProdReq { RequestImages = $"[\"{img}\",\"{img}\"]", ActionImages = "[]", CreatedAt = new DateTime(2025, 4, 1) };
        var r = new Report { ReportType = "weekly", MainAttachments = $"[\"{DataUrl(Encoding.UTF8.GetBytes("hello"), "application/pdf", "보고서 1.pdf")}\"]" };
        t.Db.AddRange(h, p, r);
        await t.Db.SaveChangesAsync();

        var dry = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: true);
        Assert.Equal(3, dry.Records);
        Assert.False(Directory.Exists(Path.Combine(_root, "기타세정")));   // 미리보기는 아무것도 안 만든다

        var res = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: false);
        Assert.Equal(3, res.Records);
        Assert.Equal(3, res.Files);   // 같은 기록 안의 같은 사진은 한 번만

        using var db = t.NewContext();
        var h2 = db.Handovers.Single();
        Assert.DoesNotContain("data:", h2.Images);
        Assert.Contains("att:9|x.png|image", h2.Images);   // 이미 파일인 것은 그대로
        var p2 = db.ProdReqs.Single();
        var refs = System.Text.Json.JsonSerializer.Deserialize<string[]>(p2.RequestImages)!;
        Assert.Equal(2, refs.Length);
        Assert.Equal(refs[0], refs[1]);

        var atts = db.Attachments.ToList();
        Assert.Equal(3, atts.Count);
        var ha = atts.Single(a => a.Folder == Path.Combine("기타세정", "2025-03"));
        Assert.Equal("handover", ha.Scope);
        Assert.StartsWith("20250302_000000_기타세정_", ha.StoredName);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], File.ReadAllBytes(store.PathOf(ha)));
        var ra = atts.Single(a => a.Scope == "reports");
        Assert.Equal("보고서 1.pdf", ra.FileName);
        Assert.Equal("file", ra.Kind);
        Assert.Equal("hello", File.ReadAllText(store.PathOf(ra)));

        // 다시 돌려도 남은 것이 없다
        var again = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: false);
        Assert.Equal(0, again.Files);
    }

    [Fact]
    public async Task 옛_GUID_이름_첨부를_분류_날짜_이름으로_옮긴다()
    {
        using var t = new TestDb();
        var store = Store();
        Directory.CreateDirectory(Path.Combine(_root, "202609"));
        var guid = Guid.NewGuid().ToString("N");
        File.WriteAllBytes(Path.Combine(_root, "202609", guid + ".jpg"), [7, 7]);
        var a = new Attachment { Folder = "202609", StoredName = guid + ".jpg", FileName = "현장 사진.jpg", ContentType = "image/jpeg",
            Kind = "image", Scope = "office", CreatedAt = new DateTime(2026, 9, 3, 10, 20, 30) };
        var gone = new Attachment { Folder = "202608", StoredName = Guid.NewGuid().ToString("N") + ".png", FileName = "x.png", Scope = "reports" };
        t.Db.Attachments.AddRange(a, gone);
        await t.Db.SaveChangesAsync();

        var dry = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: true);
        Assert.Equal(1, dry.Renamed);
        Assert.Equal(1, dry.Missing);
        Assert.True(File.Exists(Path.Combine(_root, "202609", guid + ".jpg")));

        var res = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: false);
        Assert.Equal(1, res.Renamed);

        using var db = t.NewContext();
        var moved = db.Attachments.Single(x => x.Id == a.Id);
        Assert.Equal(Path.Combine("BROKEN", "2026-09"), moved.Folder);
        Assert.Equal("20260903_102030_현장 사진.jpg", moved.StoredName);
        Assert.Equal([7, 7], File.ReadAllBytes(store.PathOf(moved)));
        Assert.False(File.Exists(Path.Combine(_root, "202609", guid + ".jpg")));
        Assert.Equal("202608", db.Attachments.Single(x => x.Id == gone.Id).Folder);   // 파일이 없는 것은 그대로
    }

    [Fact]
    public async Task 깨진_값은_그대로_두고_나머지는_옮긴다()
    {
        using var t = new TestDb();
        var store = Store();
        var h = new Handover { Vendor = "A", Content = "c", Images = $"[\"data:image/png;base64,@@@\",\"{DataUrl([1, 2])}\"]" };
        t.Db.Add(h);
        await t.Db.SaveChangesAsync();

        var res = await InlineImageMigrator.RunAsync(t.Db, store, dryRun: false);
        Assert.Equal(1, res.Files);
        Assert.Equal(1, res.Failed);
        using var db = t.NewContext();
        var images = db.Handovers.Single().Images;
        Assert.Contains("data:image/png;base64,@@@", images);
        Assert.Contains("att:", images);
    }
}
