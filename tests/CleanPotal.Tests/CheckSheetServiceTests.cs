using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>QR 체크시트 — 어떤 항목이 언제 뜨는지, 저장·제출 규칙, 수정 이력, NG, 월간 리포트.</summary>
public class CheckSheetServiceTests
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTime Local { get; set; }
        public FixedClock(DateTime local) => Local = local;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Local, DateTimeKind.Utc));
    }

    // 2026-10-07 은 수요일
    private static readonly DateOnly Wed = new(2026, 10, 7);
    private static readonly CheckActor Worker = new("w1", "홍길동", false, true);
    private static readonly CheckActor Viewer = new("v1", "구경만", false, false);
    private static readonly CheckActor Admin = new("adm", "관리자", true, true);
    private const string Photo = "att:1|a.jpg|image";

    private static (TestDb T, CheckSheetService Svc, FixedClock Clock) Make(DateTime now)
    {
        var t = new TestDb();
        CheckSheetSeed.Run(t.Db);
        var clock = new FixedClock(now);
        return (t, new CheckSheetService(t.Db, clock), clock);
    }

    private static CheckResultSaveRequest Req(DateOnly d, string shift, string? result, decimal? num = null, string? memo = null,
        IReadOnlyList<CheckPhotoDto>? photos = null, string? reason = null)
        => new(d, shift, result, num, memo, photos, true, reason);

    private static int ItemId(TestDb t, string code) => t.Db.CheckItems.AsNoTracking().Single(i => i.Code == code).Id;

    [Theory]
    [InlineData("2026-10-07 06:59", "2026-10-06", "야간")]
    [InlineData("2026-10-07 07:00", "2026-10-07", "주간")]
    [InlineData("2026-10-07 17:29", "2026-10-07", "주간")]
    [InlineData("2026-10-07 17:30", "2026-10-07", "야간")]
    [InlineData("2026-10-07 23:59", "2026-10-07", "야간")]
    public void 교대는_시작일_기준이다(string now, string date, string shift)
    {
        var (d, s) = CheckSheetService.ShiftAt(DateTime.Parse(now), new TimeOnly(7, 0), new TimeOnly(17, 30));
        Assert.Equal(DateOnly.Parse(date), d);
        Assert.Equal(shift, s);
    }

    [Fact]
    public async Task 구역_화면에는_공통_항목과_그_구역_항목이_뜨고_주간조만_항목은_야간에_빠진다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var day = (await svc.GetSheetAsync("m-out", null, null, Worker))!;
        Assert.Equal("주간", day.Shift);
        Assert.True(day.IsCurrent);
        Assert.Equal("M-001", day.Items[0].Code);                       // 공통 항목이 맨 위
        Assert.Equal("common", day.Items[0].Group);
        Assert.Contains(day.Items, i => i.Code == "M-016" && i.ResultType == "NUM" && i.SpecText == "±500V 이내");
        Assert.DoesNotContain(day.Items, i => i.Code == "M-002");        // 다른 구역 항목은 안 뜬다
        Assert.Contains(day.Items, i => i.Code == "M-027" && i.Group == "weekly" && i.DueState == "이번 주" && !i.Required);

        var night = (await svc.GetSheetAsync("M-OUT", Wed, "야간", Worker))!;
        Assert.DoesNotContain(night.Items, i => i.Code == "M-016");
        Assert.Null(await svc.GetSheetAsync("M-ALL", null, null, Worker));   // 공통 구역은 QR 화면이 없다
    }

    [Fact]
    public async Task 수치는_기준으로_판정하고_NG_는_미조치로_남는다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var id = ItemId(t, "M-016");
        var ok = await svc.SaveResultAsync("M-OUT", id, Req(Wed, "주간", "NG", num: -480), Worker);
        Assert.Equal("OK", ok!.Result);                                  // 화면이 NG 를 보내도 값이 기준 안이면 OK
        var ng = await svc.SaveResultAsync("M-OUT", id, Req(Wed, "주간", null, num: 620, memo: "이오나이저 점검"), Worker);
        Assert.Equal("NG", ng!.Result);
        Assert.Equal("OPEN", ng.NgStatus);
        var list = await svc.GetNgsAsync(true, null, null, null);
        Assert.Equal("M-016", Assert.Single(list).ItemCode);

        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.CloseNgAsync(list[0].ResultId, " ", Worker));
        var closed = await svc.CloseNgAsync(list[0].ResultId, "이오나이저 청소 후 재측정 120V", Worker);
        Assert.Equal("DONE", closed.NgStatus);
        Assert.Empty(await svc.GetNgsAsync(true, null, null, null));
    }

    [Fact]
    public async Task 권한과_입력_규칙()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var id = ItemId(t, "M-011");
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveResultAsync("M-OUT", id, Req(Wed, "주간", "OK"), Viewer));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("M-OUT", id, Req(Wed, "주간", "NA"), Worker));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("M-OUT", ItemId(t, "M-002"), Req(Wed, "주간", "OK"), Worker));
        // 바로 앞 교대(전날 야간)는 되지만, 그보다 앞은 안 된다.
        await svc.SaveResultAsync("M-OUT", id, Req(Wed.AddDays(-1), "야간", "OK"), Worker);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveResultAsync("M-OUT", id, Req(Wed.AddDays(-1), "주간", "OK"), Worker));
        // 빈 결과로 저장하면 지운다.
        Assert.Null(await svc.SaveResultAsync("M-OUT", id, Req(Wed.AddDays(-1), "야간", ""), Worker));
        Assert.Equal(0, await t.Db.CheckResults.CountAsync());
    }

    private static async Task FillAll(CheckSheetService svc, string zone, DateOnly d, string shift, CheckActor who)
    {
        var sheet = (await svc.GetSheetAsync(zone, d, shift, who))!;
        foreach (var i in sheet.Items.Where(i => i.Required))
        {
            var photos = i.PhotoPolicy == "작업 전·후"
                ? new[] { new CheckPhotoDto("before", Photo), new CheckPhotoDto("after", Photo) }
                : Array.Empty<CheckPhotoDto>();
            await svc.SaveResultAsync(zone, i.ItemId, Req(d, shift, "OK", num: i.ResultType == "NUM" ? 100 : null, photos: photos), who);
        }
    }

    [Fact]
    public async Task 제출은_빠진_필수항목과_사진을_확인하고_이후에는_관리자만_사유를_적고_고친다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SubmitAsync("M-OUT", new(Wed, "주간", true), Worker));
        Assert.Contains("입력한 항목이 없습니다", ex.Message);

        var idNg = ItemId(t, "M-012");
        await svc.SaveResultAsync("M-OUT", idNg, Req(Wed, "주간", "NG"), Worker);
        ex = await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SubmitAsync("M-OUT", new(Wed, "주간", true), Worker));
        Assert.Contains("M-012 TABLE 청소 하였는가? — NG 사유 필요", ex.Message);
        Assert.Contains("M-012 TABLE 청소 하였는가? — NG 사진 필요", ex.Message);
        Assert.Contains("결과 미입력", ex.Message);

        await FillAll(svc, "M-OUT", Wed, "주간", Worker);
        await svc.SaveResultAsync("M-OUT", idNg, Req(Wed, "주간", "NG", memo: "비닐 찢어짐", photos: new[] { new CheckPhotoDto("ng", Photo) }), Worker);
        var done = await svc.SubmitAsync("M-OUT", new(Wed, "주간", true), Worker);
        Assert.NotNull(done.SubmittedAt);
        Assert.False(done.CanEdit);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.SaveResultAsync("M-OUT", idNg, Req(Wed, "주간", "OK"), Worker));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("M-OUT", idNg, Req(Wed, "주간", "OK"), Admin));
        await svc.SaveResultAsync("M-OUT", idNg, Req(Wed, "주간", "OK", reason: "오입력"), Admin);
        var audit = Assert.Single(t.Db.ContentAudits.AsNoTracking().Where(a => a.EntityType == CheckSheetService.AuditType));
        Assert.Contains("NG", audit.Detail);
        Assert.Contains("사유: 오입력", audit.Detail);
    }

    [Fact]
    public async Task 주_1회_항목은_지정_요일부터_해당되고_한_번_하면_그_주에는_끝난다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var item = t.Db.CheckItems.Single(i => i.Code == "M-027");
        item.Weekday = 2;   // 화요일 — 수요일인 오늘은 "밀림"
        t.Db.SaveChanges();

        var sheet = (await svc.GetSheetAsync("M-OUT", Wed, "주간", Worker))!;
        var row = sheet.Items.Single(i => i.Code == "M-027");
        Assert.Equal("밀림", row.DueState);
        Assert.True(row.Required);

        await svc.SaveResultAsync("M-OUT", row.ItemId, Req(Wed, "주간", "OK",
            photos: new[] { new CheckPhotoDto("before", Photo), new CheckPhotoDto("after", Photo) }), Worker);
        var night = (await svc.GetSheetAsync("M-OUT", Wed, "야간", Worker))!;
        var again = night.Items.Single(i => i.Code == "M-027");
        Assert.Equal("완료", again.DueState);
        Assert.False(again.Required);
        Assert.Contains("홍길동", again.DoneElsewhere);
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveResultAsync("M-OUT", row.ItemId, Req(Wed, "야간", "OK"), Admin));
    }

    [Fact]
    public async Task 현황과_월간_리포트()
    {
        var (t, svc, clock) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        await svc.SaveSettingsAsync(new Dictionary<string, string> { ["EffectiveDate"] = "2026-10-06" });
        await FillAll(svc, "M-OUT", Wed, "주간", Worker);
        await svc.SubmitAsync("M-OUT", new(Wed, "주간", true), Worker);

        var status = await svc.GetStatusAsync(Wed);
        var outZone = status.Lines.Single(l => l.Line == "METAL").Zones.Single(z => z.Code == "M-OUT");
        Assert.Equal("submitted", outZone.Day.State);
        Assert.Equal(outZone.Day.Total, outZone.Day.Done);
        Assert.Equal("none", outZone.Night.State);

        clock.Local = new DateTime(2026, 10, 8, 9, 0, 0);
        var report = await svc.GetReportAsync("METAL", 2026, 10);
        Assert.Equal(31, report.Days);
        var pass = report.Rows.Single(r => r.ZoneCode == "M-OUT" && r.ItemCode == "M-015");
        Assert.Equal("", pass.Cells[(5 - 1) * 2]);        // 10/5 — 시행일 전이라 비움
        Assert.Equal("미", pass.Cells[(6 - 1) * 2]);      // 10/6 주간 — 안 함
        Assert.Equal("O", pass.Cells[(7 - 1) * 2]);       // 10/7 주간 — 함
        Assert.Equal("미", pass.Cells[(7 - 1) * 2 + 1]);  // 10/7 야간 — 안 함
        Assert.Equal("", pass.Cells[(8 - 1) * 2]);        // 10/8 주간 — 아직 진행 중
        var meter = report.Rows.Single(r => r.ZoneCode == "M-OUT" && r.ItemCode == "M-016");
        Assert.Equal("100", meter.Cells[(7 - 1) * 2]);
        Assert.Equal("", meter.Cells[(7 - 1) * 2 + 1]);   // 주간조만 항목은 야간 칸이 없다
        Assert.Contains(report.Rows, r => r.ZoneCode == "M-IN" && r.ItemCode == "M-001");   // 공통 항목은 구역마다
    }

    [Fact]
    public async Task 엑셀_가져오기는_코드로_넣거나_고치고_새_항목에는_번호를_붙인다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        var zones = new[]
        {
            new CheckZoneDto(0, "N-OUT", "출고검사실", "N-METAL", 1, false, true, "출입문 오른쪽", 1, true, ""),
            new CheckZoneDto(0, "bad code!", "x", "N-METAL", 2, false, true, "", 1, true, ""),
        };
        var items = new[]
        {
            new CheckItemDto(0, "", "N-OUT", 1, "TABLE 청소 하였는가?", "", "", "주·야 각 1회", null, "OKNG", "", null, null, "NONE",
                "NG 시", true, false, "5S CHECK SHEET", "", null, null, "초기 등록", true, "", default, ""),
            new CheckItemDto(0, "M-015", "M-OUT", 15, "PASS BOX 청소 하였는가?", "내부·외부", "", "주·야 각 1회", null, "OKNG", "", null, null,
                "NONE", "NG 시", true, true, "5S CHECK SHEET", "품질팀", null, null, "N/A 허용", true, "", default, ""),
            new CheckItemDto(0, "", "N-NONE", 1, "없는 구역", "", "", "주·야 각 1회", null, "OKNG", "", null, null, "NONE",
                "NG 시", true, false, "", "", null, null, "", true, "", default, ""),
        };
        var r = await svc.ImportAsync(new CheckImportRequest(zones, items), Admin);
        Assert.Equal(1, r.ZonesAdded);
        Assert.Equal(1, r.ItemsAdded);
        Assert.Equal(1, r.ItemsUpdated);
        Assert.Equal(2, r.Warnings.Count);
        Assert.True(t.Db.CheckItems.Any(i => i.Code == "N-001" && i.ZoneCode == "N-OUT"));
        var pass = t.Db.CheckItems.AsNoTracking().Single(i => i.Code == "M-015");
        Assert.True(pass.AllowNa);
        Assert.Equal("품질팀", pass.NgDept);
    }

    [Fact]
    public async Task 설정은_형식을_확인한다()
    {
        var (t, svc, _) = Make(new DateTime(2026, 10, 7, 9, 0, 0));
        using var _t = t;
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveSettingsAsync(new Dictionary<string, string> { ["DayStart"] = "7시" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => svc.SaveSettingsAsync(new Dictionary<string, string> { ["NightStart"] = "06:00" }));
        var s = await svc.SaveSettingsAsync(new Dictionary<string, string> { ["QrBaseUrl"] = "http://cleanpotal:8713/", ["Unknown"] = "x" });
        Assert.Equal("http://cleanpotal:8713", s["QrBaseUrl"]);
        Assert.False(s.ContainsKey("Unknown"));
    }
}

public class QrSvgTests
{
    [Fact]
    public void 주소를_QR_SVG_로_그린다()
    {
        var svg = CleanPotal.Api.Infrastructure.QrSvg.Render("http://10.10.10.119:8713/c/M-OUT");
        Assert.StartsWith("<svg", svg);
        Assert.Contains("viewBox=\"0 0 ", svg);
        Assert.Contains("h1v1h-1z", svg);
    }
}
