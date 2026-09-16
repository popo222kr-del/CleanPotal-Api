using CleanPotal.Api.Controllers;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Domain.Enums;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// OPER 검사값 표가 화면에 어떤 입력칸으로 나가는지, SPEC 을 벗어난 값을 실제로 잡아내는지.
///
/// 이 둘은 작업자가 잘못된 값을 그대로 다음 공정으로 넘기지 못하게 막는 자리다.
/// 규칙 자체는 Domain(InspectionValueRules)에 있고 여기서 보는 것은 "포털이 그 규칙을 제대로
/// 태우고 있는가" 다 — 화면에 옮겨 적지 않기로 한 약속이 지켜지는지.
/// </summary>
public class MesInspectionRowsTests
{
    private static InspectionParameterRowDto Row(
        ParameterType type, string code = "PARTICLE_A", string desc = "표면먼지",
        decimal? min = null, decimal? max = null, string? value = null, int valueCount = 1)
        => new(1, code, desc, min, max, value, null, null, type, valueCount);

    private static InspectionPanelDto Panel(
        bool showsFi, bool inEditable, params InspectionParameterRowDto[] rows)
        // 위치 인자 순서: LotId · LotNumber · MatId · MatDesc · Sn · ClnCount ·
        //                RecipeDefinitionId · RecipeId · PmResId · ResId · CmtAets
        => new(1, "LOT1", "MAT", "DESC", "SN", 1, null, null, null, null, null,
               ShowsInInsp: true, InInspEditable: inEditable, ShowsFiInsp: showsFi,
               InInspRows: showsFi ? Array.Empty<InspectionParameterRowDto>() : rows,
               FiInspRows: showsFi ? rows : Array.Empty<InspectionParameterRowDto>());

    // ── 어떤 입력칸을 그리는가 ──

    [Fact]
    public void 외관은_YN_칸이고_값이_없으면_N_으로_시작한다()
    {
        var row = MesInspectionRows.ToRow(Row(ParameterType.Boolean, code: "VISUAL", desc: "외관"));

        Assert.True(row.IsYesNo);
        Assert.False(row.IsOkNgCc);
        Assert.False(row.IsMultiPoint);
        // 작업자가 아무것도 안 골라도 "N" 으로 남는 것이 MES 의 기본값이다.
        Assert.Equal("N", row.InputValue);
    }

    [Fact]
    public void 판정은_OKNGCC_칸이고_비어_있는_채로_시작한다()
    {
        var row = MesInspectionRows.ToRow(Row(ParameterType.Choice, code: "RESULT", desc: "합부판정"));

        Assert.True(row.IsOkNgCc);
        // 합격/불합격은 사람이 직접 골라야 한다 — 기본값을 넣으면 안 본 채로 넘어간다.
        Assert.Null(row.InputValue);
    }

    [Fact]
    public void 다측정은_저장값을_칸별로_쪼개_보여_준다()
    {
        var row = MesInspectionRows.ToRow(
            Row(ParameterType.Numeric, code: "PARTICLE", desc: "표면먼지", value: "1|2|3", valueCount: 3));

        Assert.True(row.IsMultiPoint);
        Assert.Equal(new[] { "A", "B", "C" }, row.Points.Select(p => p.Label));
        Assert.Equal(new[] { "1", "2", "3" }, row.Points.Select(p => p.Value));
    }

    [Fact]
    public void 측정_점이_하나면_다측정이_아니다()
    {
        var row = MesInspectionRows.ToRow(
            Row(ParameterType.Numeric, code: "PARTICLE", desc: "표면먼지", value: "5", valueCount: 1));

        Assert.False(row.IsMultiPoint);
        Assert.Empty(row.Points);
        Assert.Equal("5", row.InputValue);
    }

    // ── SPEC 판정 ──

    [Fact]
    public void 표면먼지는_MAX_를_넘으면_걸린다()
    {
        // 표면먼지는 판정 방향이 반대다 — 많을수록 나쁘다.
        var row = Row(ParameterType.Numeric, code: "PARTICLE", desc: "표면먼지", max: 10);

        Assert.NotNull(MesInspectionRows.SpecOutReason(row, "11"));
        Assert.Null(MesInspectionRows.SpecOutReason(row, "9"));
    }

    [Fact]
    public void 보통_계측값은_MIN_이하면_걸린다()
    {
        var row = Row(ParameterType.Numeric, code: "THICKNESS", desc: "두께", min: 5);

        Assert.NotNull(MesInspectionRows.SpecOutReason(row, "5"));   // 이하이므로 걸린다
        Assert.NotNull(MesInspectionRows.SpecOutReason(row, "4"));
        Assert.Null(MesInspectionRows.SpecOutReason(row, "6"));
    }

    [Fact]
    public void 미측정은_걸리지_않는다()
    {
        var row = Row(ParameterType.Numeric, code: "THICKNESS", desc: "두께", min: 5);

        // 아직 재지 않은 값까지 SPEC OUT 으로 잡으면 입력 도중에 계속 경고가 뜬다.
        Assert.Null(MesInspectionRows.SpecOutReason(row, null));
        Assert.Null(MesInspectionRows.SpecOutReason(row, "  "));
    }

    [Fact]
    public void 다측정은_한_점만_벗어나도_걸리고_어느_점인지_알려_준다()
    {
        var row = Row(ParameterType.Numeric, code: "PARTICLE", desc: "표면먼지", max: 10, valueCount: 3);

        var reason = MesInspectionRows.SpecOutReason(row, "1|99|3");

        Assert.NotNull(reason);
        Assert.Contains("[B]", reason!);   // 두 번째 점
    }

    [Fact]
    public void 출고검사_화면은_FI_표만_판정한다()
    {
        // 7000 에서는 IN INSP 가 읽기전용으로 같이 보인다. 고칠 수도 없는 값 때문에 실행이 막히면 안 된다.
        var fi = Row(ParameterType.Numeric, code: "THICKNESS", desc: "두께", min: 5);
        var panel = Panel(showsFi: true, inEditable: false, fi);

        var outOfSpec = MesInspectionRows.SpecOut(panel, new[] { new MesInspInputDto(1, "1", null) });

        Assert.Single(outOfSpec);
        Assert.Equal(1, outOfSpec[0].ParameterDefinitionId);
    }

    [Fact]
    public void 읽기전용_표뿐이면_아무것도_판정하지_않는다()
    {
        var row = Row(ParameterType.Numeric, code: "THICKNESS", desc: "두께", min: 5);
        var panel = Panel(showsFi: false, inEditable: false, row);

        Assert.Empty(MesInspectionRows.SpecOut(panel, new[] { new MesInspInputDto(1, "1", null) }));
    }

    [Fact]
    public void 보내지_않은_항목은_판정에서_빠진다()
    {
        // 화면이 보낸 값만 본다 — 안 보낸 것은 그 화면에서 다루지 않는 항목이다.
        var row = Row(ParameterType.Numeric, code: "THICKNESS", desc: "두께", min: 5);
        var panel = Panel(showsFi: false, inEditable: true, row);

        Assert.Empty(MesInspectionRows.SpecOut(panel, Array.Empty<MesInspInputDto>()));
    }
}
