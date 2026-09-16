using ProductionManagement.Domain.BusinessRules;
using ProductionManagement.Domain.Enums;
using Xunit;

namespace CleanPotal.Tests;

/// <summary>
/// OPER "실행" 직전 게이트. 작업자가 잘못된 상태로 다음 공정에 넘기지 못하게 막는 자리다.
///
/// 규칙은 데스크톱판에서 옮겨 온 것인데 이 저장소에는 그 시절 테스트가 같이 오지 않았다.
/// 포털이 이 규칙을 그대로 부르므로(MesOperController.Execute) 여기서 고정한다.
/// </summary>
public class MesOperGateTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 10, 0, 0);

    // ── 세정·건조 READ TIME ──

    [Fact]
    public void 레시피_시간이_안_찼으면_완료로_넘길_수_없다()
    {
        var message = OperExecutionRules.GetReadTimeBlockMessage(
            3000, TranCode.End, readTimeMinutes: 60, cmtAets: null,
            startedAt: Now.AddMinutes(-20), now: Now);

        Assert.NotNull(message);
        Assert.Contains("60분", message!);
        Assert.Contains("20분", message!);   // 몇 분 남았는지 알려 줘야 기다릴 수 있다
    }

    [Fact]
    public void 시간이_찼으면_넘어간다()
    {
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(
            3000, TranCode.End, 60, null, Now.AddMinutes(-60), Now));
    }

    [Fact]
    public void 특이사항_코멘트를_적으면_시간이_안_찼어도_넘어간다()
    {
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(
            3000, TranCode.End, 60, "설비 이상으로 조기 종료", Now.AddMinutes(-5), Now));
    }

    [Theory]
    [InlineData(TranCode.Start)]     // 같은 공정 안의 전이는 검사하지 않는다
    [InlineData(TranCode.Hold)]
    [InlineData(TranCode.Release)]
    public void 완료가_아닌_전이는_READ_TIME_을_보지_않는다(TranCode tranCode)
    {
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(
            3000, tranCode, 60, null, Now.AddMinutes(-1), Now));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    [InlineData(4100)]   // Laser&CO2 는 레시피를 쓰지만 READ TIME 은 걸지 않는다
    [InlineData(5000)]
    [InlineData(7000)]
    public void 세정_건조가_아닌_공정은_READ_TIME_을_보지_않는다(int operCode)
    {
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(
            operCode, TranCode.End, 60, null, Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void 레시피에_READ_TIME_이_없으면_기다리게_하지_않는다()
    {
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(3000, TranCode.End, null, null, Now.AddMinutes(-1), Now));
        Assert.Null(OperExecutionRules.GetReadTimeBlockMessage(3000, TranCode.End, 0, null, Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void 시작_시각을_모르면_아직_안_찬_것으로_본다()
    {
        // 경과 0분으로 본다 — 모르는 것을 "다 됐다" 로 넘기면 덜 마른 물건이 나간다.
        Assert.NotNull(OperExecutionRules.GetReadTimeBlockMessage(3000, TranCode.End, 60, null, null, Now));
    }

    // ── 세정·건조 레시피·설비 ──

    [Theory]
    [InlineData(3000, false, "RES-1", true)]    // 레시피가 없다
    [InlineData(3000, true, null, true)]        // 설비가 없다
    [InlineData(3000, true, "   ", true)]       // 공백은 없는 것이다
    [InlineData(3000, true, "RES-1", false)]
    [InlineData(4000, false, null, true)]
    [InlineData(4100, false, null, false)]      // Laser&CO2 는 레시피를 쓰지만 필수는 아니다
    [InlineData(2000, false, null, false)]
    public void 세정_건조는_레시피와_설비가_모두_있어야_한다(
        int operCode, bool hasRecipe, string? resId, bool blocked)
    {
        Assert.Equal(blocked, OperExecutionRules.IsMissingRecipeOrEquipment(operCode, hasRecipe, resId));
    }

    // ── 출고검사 NG · 출력 관리 ──

    [Theory]
    [InlineData(7000, TranCode.End, "NG", true)]
    [InlineData(7000, TranCode.End, "ng", true)]     // 대소문자를 가리지 않는다
    [InlineData(7000, TranCode.End, " NG ", true)]
    [InlineData(7000, TranCode.End, "OK", false)]
    [InlineData(7000, TranCode.End, "CC", false)]
    [InlineData(7000, TranCode.End, null, false)]
    [InlineData(7000, TranCode.Hold, "NG", false)]   // 완료로 넘길 때만 묻는다
    [InlineData(2100, TranCode.End, "NG", false)]    // 입고검사는 묻지 않는다
    public void 출고검사_부적합은_한_번_더_묻는다(int operCode, TranCode tranCode, string? value, bool asks)
    {
        Assert.Equal(asks, OperExecutionRules.RequiresOutgoingNgConfirmation(operCode, tranCode, value));
    }

    [Theory]
    [InlineData(2100, TranCode.End, true)]
    [InlineData(2100, TranCode.Ship, true)]
    [InlineData(7000, TranCode.End, true)]
    [InlineData(7000, TranCode.Ship, true)]
    [InlineData(7000, TranCode.Hold, false)]
    [InlineData(3000, TranCode.End, false)]
    public void 검사_공정은_출력_관리를_먼저_띄운다(int operCode, TranCode tranCode, bool defers)
    {
        Assert.Equal(defers, OperExecutionRules.DefersToOutputManagement(operCode, tranCode));
    }

    // ── 사유 코드 ──

    [Theory]
    [InlineData(TranCode.Hold, true)]
    [InlineData(TranCode.Release, true)]
    [InlineData(TranCode.Rework, true)]
    [InlineData(TranCode.Skip, true)]
    [InlineData(TranCode.Ship, true)]
    [InlineData(TranCode.Start, false)]
    [InlineData(TranCode.End, false)]
    public void 사유가_필요한_전이(TranCode tranCode, bool required)
    {
        Assert.Equal(required, OperExecutionRules.RequiresReasonCode(tranCode));
    }

    [Fact]
    public void 전이를_고르지_않았으면_사유도_묻지_않는다()
    {
        Assert.False(OperExecutionRules.RequiresReasonCode(null));
    }

    // ── 게이트 순서 ──

    /// <summary>
    /// READ TIME 차단과 출고검사 NG 확인은 순서가 문제 되지 않는다 — 같이 걸릴 수 있는 공정이 없기 때문이다.
    /// 이 전제가 깨지면(예: 7000 을 세정·건조처럼 다루면) 어느 쪽을 먼저 보느냐로 동작이 갈리므로,
    /// 그때는 순서를 정하고 화면과 서버를 함께 맞춰야 한다.
    /// </summary>
    [Fact]
    public void READ_TIME_과_출고검사_NG_는_같은_공정에서_함께_걸리지_않는다()
    {
        foreach (var operCode in new[] { 2000, 2100, 3000, 4000, 4100, 5000, 7000, 7100, 8100 })
        {
            var readTime = OperExecutionRules.GetReadTimeBlockMessage(
                operCode, TranCode.End, 60, null, Now.AddMinutes(-1), Now) is not null;
            var ng = OperExecutionRules.RequiresOutgoingNgConfirmation(operCode, TranCode.End, "NG");
            Assert.False(readTime && ng, $"OPER {operCode} 에서 두 게이트가 함께 걸린다 — 순서를 정해야 한다.");
        }
    }
}
