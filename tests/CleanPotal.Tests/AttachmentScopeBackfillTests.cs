using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>영역 칸이 생기기 전에 올린 첨부에 영역을 채운다 — 받을 때 그 화면의 조회 권한을 보게 된다.</summary>
public class AttachmentScopeBackfillTests
{
    private static Attachment Att() => new() { StoredName = "x", Folder = "202609", FileName = "a.png", ContentType = "image/png", Kind = "image" };

    [Fact]
    public void 주간보고와_BROKEN_에서_쓰인_첨부에_영역을_채운다()
    {
        using var t = new TestDb();
        var a = Att(); var b = Att(); var shared = Att(); var orphan = Att();
        t.Db.Attachments.AddRange(a, b, shared, orphan);
        t.Db.SaveChanges();
        t.Db.Reports.Add(new Report { ReportType = "weekly", MemoAttachments = $"[\"att:{a.Id}|a.png|image\",\"att:{shared.Id}|s|file\"]" });
        t.Db.BrokenRecords.Add(new BrokenRecord { IncidentReports = $"[\"att:{b.Id}|b.png|image\"]", TrainingDocs = $"[\"att:{shared.Id}|s|file\"]" });
        t.Db.SaveChanges();

        AttachmentScopeBackfill.Run(t.Db);
        t.Db.ChangeTracker.Clear();

        Assert.Equal("reports", t.Db.Attachments.Find(a.Id)!.Scope);
        Assert.Equal("office", t.Db.Attachments.Find(b.Id)!.Scope);
        Assert.Equal("", t.Db.Attachments.Find(shared.Id)!.Scope);   // 두 영역에서 같이 쓰이면 막지 않는다
        Assert.Equal("", t.Db.Attachments.Find(orphan.Id)!.Scope);   // 어디서도 안 쓰이면 그대로
    }
}
