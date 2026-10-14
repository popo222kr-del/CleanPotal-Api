using CleanPotal.Api.Infrastructure;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>입고검사 이력 — 회차 요약·경고·대기 배지(기존 MES 측정값 기준).</summary>
public class IncomingInspectionTests
{
    private static LegacyEdc E(string seq, string param, string oper, params string[] values)
        => new(seq, param, oper, values.ToList(), "u", "20250520223713");

    private static readonly IReadOnlyList<IncomingInspection.SpecLine> Spec =
    [
        new("WEIGHT", "2100", "무게(g)", "3800", "3900"),
        new("PARTICLE", "7000", "표면먼지", "0.00", "5.00"),
    ];

    [Fact]
    public void 회차_요약_무게_식각량_외관_먼지_판정_스펙밖()
    {
        var lot = new LegacyLot { LotId = "1SS250520028P", UseCount = "11", CreateTime = "20250520162926", Factory = "CUSTOMER", Comment = "메모" };
        var c = IncomingInspection.BuildCycle(lot, new[]
        {
            E("3", "CHIP", "2100", "Y"), E("3", "SCR", "2100", "N"), E("3", "WEIGHT", "2100", "4120.00"),
            E("9", "WEIGHT", "7000", "4110"), E("9", "PARTICLE", "7000", "0", "7", "0"), E("9", "RESULT", "7000", "NG"),
            E("1", "NTHK", "RCV", "253.00"),
        }, Spec, current: false);

        Assert.Equal("2025-05-20", c.InDay);
        Assert.Equal("출하완료", c.Status);
        Assert.Equal("10", c.Etch);
        Assert.Equal(new[] { "CHIP" }, c.InVisual);
        Assert.Empty(c.OutVisual);
        Assert.Equal("0/7/0", c.Particle);
        Assert.Equal("NG", c.Result);
        Assert.Contains("입고 무게(g) 4120.00 (기준 3800~3900)", c.OutOfSpec);
        Assert.Contains("출하 표면먼지 7 (기준 0.00~5.00)", c.OutOfSpec);
        Assert.Equal("메모", c.Comment);
    }

    [Fact]
    public void 같은_항목을_다시_쟀으면_마지막_값()
    {
        var c = IncomingInspection.BuildCycle(new LegacyLot { LotId = "L" },
            new[] { E("3", "WEIGHT", "2100", "4000"), E("5", "WEIGHT", "2100", "3850") }, Spec, current: true);
        Assert.Equal("3850", c.WeightIn);
        Assert.Empty(c.OutOfSpec);
    }

    [Fact]
    public void 경고는_직전_NG_와_지난_회차_외관_스펙밖()
    {
        IncomingInspection.Cycle C(string use, string? result, string[] inVis, params string[] oos)
            => new("L" + use, use, "2025-01-01", null, "출하완료", false, null, null, null, inVis.ToList(), new(), null, null, result, oos.ToList(), "");
        var w = IncomingInspection.Warnings(new[]
        {
            C("10", "NG", [], "출하 표면먼지 7 (기준 0.00~5.00)"),
            C("9", "OK", []),
            C("8", "OK", ["SCR"]),
        });
        Assert.Equal("직전(10회) 판정 NG", w[0]);
        Assert.Contains("10회 출하 표면먼지 7 (기준 0.00~5.00)", w);
        Assert.Contains("8회 입고 외관 이상 — 긁힘", w);
        Assert.Empty(IncomingInspection.Warnings(Array.Empty<IncomingInspection.Cycle>()));
    }

    [Fact]
    public void 대기_목록_배지()
    {
        Assert.Equal(new[] { "직전 NG", "직전 외관 깨짐" },
            IncomingInspection.Flags(new[] { E("9", "RESULT", "7000", "NG"), E("3", "CHIP", "2100", "Y"), E("3", "SCR", "2100", "N") }));
        Assert.Empty(IncomingInspection.Flags(new[] { E("9", "RESULT", "7000", "OK") }));
    }
}
