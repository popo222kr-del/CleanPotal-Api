using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Application.Screens;
using ProductionManagement.Domain.BusinessRules;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// MES OPER(공정) 화면 — 작업자가 실제로 LOT 을 처리하는 곳.
///
/// <b>실행 게이트는 전부 서버가 판정한다.</b> 사유코드·레시피/설비·SPEC OUT·출고검사 NG·READ TIME 은
/// 안전 장치라, 화면에 옮겨 적으면 두 곳이 갈라지는 순간 한쪽이 그냥 통과시킨다.
/// 규칙 자체는 MES 데스크톱판과 공유하는 Domain(OperExecutionRules · InspectionValueRules)이 원본이고,
/// 여기서는 그 규칙을 화면이 쓰기 좋은 순서로 부르기만 한다.
/// </summary>
[ApiController]
[Route("api/mes/oper")]
[Authorize(Policy = "ViewMes")]
public class MesOperController : ControllerBase
{
    private readonly IProcessDefinitionService _processes;
    private readonly IOperQueryService _query;
    private readonly IInspectionService _inspection;
    private readonly ITranDefinitionService _trans;
    private readonly IOperActionService _actions;
    private readonly IProductReferenceDataService _refData;
    private readonly ILogger<MesOperController> _log;

    public MesOperController(
        IProcessDefinitionService processes,
        IOperQueryService query,
        IInspectionService inspection,
        ITranDefinitionService trans,
        IOperActionService actions,
        IProductReferenceDataService refData,
        ILogger<MesOperController> log)
    {
        _processes = processes;
        _query = query;
        _inspection = inspection;
        _trans = trans;
        _actions = actions;
        _refData = refData;
        _log = log;
    }

    // ── 화면 ───────────────────────────────────────────────────────────────

    /// <summary>이 공정이 어떤 공정인지 + 화면이 알아야 할 규칙(레시피 공정인가·다중선택 되는가).</summary>
    [HttpGet("{operCode:int}")]
    public async Task<ActionResult<MesOperScreenDto>> Screen(int operCode, CancellationToken ct)
    {
        var proc = (await _processes.GetAllAsync(ct)).FirstOrDefault(p => p.OperCode == operCode);
        return Ok(new MesOperScreenDto(
            operCode,
            proc?.ProcessDefinitionId ?? 0,
            proc?.ProcessName ?? $"OPER {operCode}",
            OperScreens.All.FirstOrDefault(o => o.Code == operCode)?.Name ?? "",
            OperExecutionRules.IsRecipeOper(operCode),
            OperExecutionRules.RequiresRecipeAndEquipment(operCode),
            OperExecutionRules.SupportsMultiSelect(operCode)));
    }

    /// <summary>이 공정에 지금 있는 LOT 목록.</summary>
    [HttpGet("{operCode:int}/lots")]
    public async Task<ActionResult<IReadOnlyList<OperLotItemDto>>> Lots(
        int operCode, [FromQuery] string? keyword, CancellationToken ct)
    {
        var processDefinitionId = await ProcessIdAsync(operCode, ct);
        var lots = await _query.SearchAsync(
            new OperLotSearchRequest(processDefinitionId, string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim()),
            ct);
        return Ok(lots);
    }

    /// <summary>
    /// LOT 을 고르면 필요한 것 전부 — 제품 정보·검사값 표·레시피 후보·고를 수 있는 TRAN·배치 묶음.
    /// 검사값 표의 각 행은 <b>어떤 입력칸을 그릴지까지 서버가 정해서</b> 내려준다(Y/N · OK/NG/CC ·
    /// 다측정 A~E · 단일 숫자). 이 분기는 Domain 의 규칙이라 화면이 다시 판단할 일이 아니다.
    /// </summary>
    [HttpGet("{operCode:int}/lots/{lotId:int}/panel")]
    public async Task<ActionResult<MesOperPanelDto>> Panel(int operCode, int lotId, CancellationToken ct)
    {
        var panel = await _inspection.GetPanelAsync(lotId, ct);
        var lot = await FindLotAsync(operCode, lotId, ct);

        var recipes = lot is { ProductId: > 0 }
            ? await _refData.GetProductRecipesForOperAsync(lot.ProductId, operCode, ct)
            : Array.Empty<ProductRecipeDto>();
        var transitions = await _trans.GetAvailableTransitionsAsync(lotId, ct);
        var batch = lot is { IsBatch: true }
            ? await _query.GetBatchMembersAsync(lotId, ct)
            : Array.Empty<BatchMemberDto>();

        return Ok(new MesOperPanelDto(
            panel.LotId, panel.LotNumber, panel.MatId, panel.MatDesc, panel.Sn, panel.ClnCount,
            panel.RecipeDefinitionId, panel.PmResId, panel.ResId, panel.CmtAets, panel.CommentHistory,
            panel.ShowsInInsp, panel.InInspEditable, panel.ShowsFiInsp,
            panel.InInspRows.Select(MesInspectionRows.ToRow).ToList(),
            panel.FiInspRows.Select(MesInspectionRows.ToRow).ToList(),
            recipes.Select(r => new MesRecipeOptionDto(r.RecipeDefinitionId, r.RecipeDescription, r.ReadTimeMinutes)).ToList(),
            transitions.Select(t => new MesTranOptionDto(
                t.TransitionId, t.Description, OperExecutionRules.RequiresReasonCode(t.TranCode))).ToList(),
            batch.Select(b => new MesBatchMemberDto(b.LotNumber, b.SerialNumber)).ToList()));
    }

    /// <summary>고른 TRAN 에 필요한 사유 코드 목록. 필요 없는 TRAN 이면 빈 목록이다.</summary>
    [HttpGet("lots/{lotId:int}/reasons")]
    public async Task<ActionResult<IReadOnlyList<ReasonCodeDto>>> Reasons(
        int lotId, [FromQuery] int transitionId, CancellationToken ct)
    {
        var tran = (await _trans.GetAvailableTransitionsAsync(lotId, ct))
            .FirstOrDefault(t => t.TransitionId == transitionId);
        if (tran is null) return Ok(Array.Empty<ReasonCodeDto>());

        var category = OperExecutionRules.ReasonCategoryFor(tran.TranCode);
        return Ok(category is { } c ? await _trans.GetReasonCodesAsync(c, ct) : Array.Empty<ReasonCodeDto>());
    }

    /// <summary>
    /// 지금 입력한 값이 SPEC 을 벗어났는지만 본다(저장하지 않는다). 화면이 빨간 줄을 그리는 근거다.
    /// 판정 기준(MIN 이하 OUT, 표면먼지는 MAX 초과 OUT)을 화면에 옮겨 적지 않으려고 둔 통로다.
    /// </summary>
    [HttpPost("spec-check")]
    public async Task<ActionResult<IReadOnlyList<MesSpecOutDto>>> SpecCheck(
        [FromBody] MesOperSaveRequest request, CancellationToken ct)
    {
        var panel = await _inspection.GetPanelAsync(request.LotId, ct);
        return Ok(MesInspectionRows.SpecOut(panel, request.Inputs));
    }

    // ── 저장·실행 (MES 편집 권한) ──────────────────────────────────────────

    /// <summary>검사값·레시피·설비호기·코멘트만 저장한다. 공정은 움직이지 않는다.</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("panel")]
    public async Task<ActionResult<MesOperExecuteResultDto>> Save(
        [FromBody] MesOperSaveRequest request, CancellationToken ct)
    {
        try
        {
            await _inspection.SaveAsync(ToSaveRequest(request), ct);
            return Ok(MesOperExecuteResultDto.Done("저장되었습니다."));
        }
        catch (ValidationException ex)
        {
            return Ok(MesOperExecuteResultDto.Blocked(string.Join(" / ", ex.Errors)));
        }
    }

    /// <summary>
    /// TRAN 실행. MES 데스크톱판과 같은 순서로 막는다 —
    /// 사유코드 → 레시피·설비 → SPEC OUT(확인) → 출고검사 NG(확인) → READ TIME → 패널 저장 → 이동.
    ///
    /// 돌려주는 <c>outcome</c> 으로 화면이 무엇을 할지 정한다.
    /// <list type="bullet">
    /// <item><c>blocked</c> — 못 한다. 사유를 보여준다(확인으로 우회 불가).</item>
    /// <item><c>needsConfirm</c> — 물어보고, 사용자가 [그대로 진행] 하면 confirmed=true 로 다시 부른다.</item>
    /// <item><c>openOutput</c> — 검사 공정이라 출력 관리를 먼저 띄운다. 닫을 때 advance 를 부른다.</item>
    /// <item><c>done</c> — 끝났다.</item>
    /// </list>
    /// 막힌 것은 오류가 아니라 정상적인 판정이라 전부 200 이다.
    /// </summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{operCode:int}/execute")]
    public async Task<ActionResult<MesOperExecuteResultDto>> Execute(
        int operCode, [FromBody] MesOperExecuteRequest request, CancellationToken ct)
    {
        if (request.LotIds.Count == 0) return Ok(MesOperExecuteResultDto.Blocked("LOT 을 선택하세요."));

        // 검사값·레시피·코멘트가 붙는 LOT(아래 패널에 열린 것)과, 실제로 공정을 옮길 LOT 들은 다르다.
        // 다중선택 공정에서 체크한 것만 옮겨야 한다 — 패널에 열려 있다는 이유로 끼워 넣으면
        // 고르지 않은 LOT 이 같이 넘어간다.
        var primaryLotId = request.LotId;

        var tran = (await _trans.GetAvailableTransitionsAsync(primaryLotId, ct))
            .FirstOrDefault(t => t.TransitionId == request.TransitionId);
        if (tran is null)
            return Ok(MesOperExecuteResultDto.Blocked("고른 TRAN 을 이 LOT 에 적용할 수 없습니다. 목록을 다시 조회하세요."));

        if (OperExecutionRules.RequiresReasonCode(tran.TranCode) && string.IsNullOrWhiteSpace(request.ReasonCode))
            return Ok(MesOperExecuteResultDto.Blocked("사유 코드를 선택하세요."));

        if (OperExecutionRules.IsMissingRecipeOrEquipment(operCode, request.RecipeDefinitionId is not null, request.ResId))
            return Ok(MesOperExecuteResultDto.Blocked("세정/건조 공정은 RECIPE ID 와 RES ID 를 모두 입력해야 실행할 수 있습니다."));

        var panel = await _inspection.GetPanelAsync(primaryLotId, ct);

        if (!request.Confirmed)
        {
            var specOut = MesInspectionRows.SpecOut(panel, request.Inputs);
            if (specOut.Count > 0)
                return Ok(MesOperExecuteResultDto.NeedsConfirm(
                    "SPEC OUT 항목이 있습니다.\n\n" + string.Join("\n", specOut.Select(s => s.Reason))));

            // 출고검사(7000) 합부판정이 NG 면 한 번 더 묻는다.
            // 원본은 합부판정 칸 중 "첫 줄" 의 값만 본다. 여기서는 값이 들어 있는 칸 중 첫 줄을 본다 —
            // 합부판정 칸이 둘 이상인 제품에서 첫 칸이 비어 있으면 뒤의 NG 를 못 보고 넘어가기 때문이다.
            // 더 묻는 쪽이라 [그대로 진행] 으로 지나갈 수 있고, 막지는 않는다.
            var judgement = panel.FiInspRows
                .Where(r => InspectionValueRules.IsOkNgCc(r.ParameterType))
                .Select(r => ValueOf(request.Inputs, r.ParameterDefinitionId))
                .FirstOrDefault(v => v is not null);
            if (OperExecutionRules.RequiresOutgoingNgConfirmation(operCode, tran.TranCode, judgement))
                return Ok(MesOperExecuteResultDto.NeedsConfirm("출고검사 합부판정이 부적합(NG)입니다."));
        }

        // READ TIME 은 확인으로 넘어갈 수 없다(데스크톱판과 같다).
        var lot = await FindLotAsync(operCode, primaryLotId, ct);
        var readTime = lot is { ProductId: > 0 }
            ? (await _refData.GetProductRecipesForOperAsync(lot.ProductId, operCode, ct))
                .FirstOrDefault(r => r.RecipeDefinitionId == request.RecipeDefinitionId)?.ReadTimeMinutes
            : null;
        var block = OperExecutionRules.GetReadTimeBlockMessage(
            operCode, tran.TranCode, readTime, request.CmtAets, lot?.StartedAt, DateTime.Now);
        if (block is not null) return Ok(MesOperExecuteResultDto.Blocked(block));

        try
        {
            await _inspection.SaveAsync(ToSaveRequest(request), ct);

            // 입고·출고검사를 완료/출하로 넘길 때는 출력 관리를 먼저 띄우고, 닫는 시점에 전산이 이동한다.
            if (OperExecutionRules.DefersToOutputManagement(operCode, tran.TranCode))
                return Ok(MesOperExecuteResultDto.OpenOutput(
                    primaryLotId, panel.LotNumber,
                    "출력 관리 창에서 처리한 뒤 [닫기] 를 누르면 전산이 이동합니다."));

            return Ok(await AdvanceCoreAsync(request.LotIds, request.TransitionId, request.ReasonCode, tran.Description, ct));
        }
        catch (ValidationException ex)
        {
            return Ok(MesOperExecuteResultDto.Blocked(string.Join(" / ", ex.Errors)));
        }
    }

    /// <summary>출력 관리를 닫은 뒤 실제로 공정을 옮긴다(실행에서 미뤄 둔 몫).</summary>
    [Authorize(Policy = "EditMes")]
    [HttpPost("{operCode:int}/advance")]
    public async Task<ActionResult<MesOperExecuteResultDto>> Advance(
        int operCode, [FromBody] MesOperAdvanceRequest request, CancellationToken ct)
    {
        if (request.LotIds.Count == 0) return Ok(MesOperExecuteResultDto.Blocked("LOT 을 선택하세요."));

        var tran = (await _trans.GetAvailableTransitionsAsync(request.LotIds[0], ct))
            .FirstOrDefault(t => t.TransitionId == request.TransitionId);
        if (tran is null)
            return Ok(MesOperExecuteResultDto.Blocked("고른 TRAN 을 이 LOT 에 적용할 수 없습니다. 목록을 다시 조회하세요."));
        // 이 통로는 실행(Execute)이 출력 관리로 미뤄 둔 이동만 마무리한다. 그 밖의 TRAN 을 여기로 보내면
        // READ TIME·레시피·설비 확인을 모두 건너뛰게 되므로 막는다(그런 이동은 Execute 로 해야 한다).
        if (!OperExecutionRules.DefersToOutputManagement(operCode, tran.TranCode))
            return Ok(MesOperExecuteResultDto.Blocked("이 TRAN 은 출력 관리를 거치는 이동이 아닙니다. [실행] 으로 처리하세요."));

        return Ok(await AdvanceCoreAsync(request.LotIds, request.TransitionId, request.ReasonCode, tran.Description, ct));
    }

    // ── 내부 ───────────────────────────────────────────────────────────────

    private async Task<MesOperExecuteResultDto> AdvanceCoreAsync(
        IReadOnlyList<int> lotIds, int transitionId, string? reasonCode, string tranDescription, CancellationToken ct)
    {
        // 어디까지 갔는지 알려 주기 위해 센다. 여러 건을 한 번에 옮기다 중간에서 멈추면,
        // 몇 건이 이미 넘어갔는지 모르면 작업자가 처음부터 다시 고르다 같은 LOT 을 두 번 올린다.
        var done = 0;
        try
        {
            // 다중선택 공정은 고른 LOT 을 한 건씩 옮긴다. 한 건이 실패하면 거기서 멈춘다 —
            // 나머지를 밀어붙이면 어디까지 갔는지 알 수 없는 상태가 된다.
            foreach (var lotId in lotIds)
            {
                await _actions.ExecuteTranAsync(new OperExecuteTranRequest(lotId, transitionId, reasonCode, 0, null), ct);
                done++;
            }

            return MesOperExecuteResultDto.Done($"{tranDescription} 처리되었습니다. ({lotIds.Count}건)");
        }
        catch (ValidationException ex)
        {
            return MesOperExecuteResultDto.Blocked(Partial(done, lotIds.Count) + string.Join(" / ", ex.Errors));
        }
        catch (InvalidOperationException ex)
        {
            return MesOperExecuteResultDto.Blocked(Partial(done, lotIds.Count) + ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MES TRAN 실행 실패 (전이 {TransitionId}, {Done}/{Total}건 처리 후)",
                transitionId, done, lotIds.Count);
            return MesOperExecuteResultDto.Blocked(
                Partial(done, lotIds.Count) + "처리 중 문제가 발생했습니다. 관리자에게 문의하세요.");
        }
    }

    /// <summary>여러 건 중 일부만 넘어갔을 때 그 사실을 문구 앞에 붙인다. 한 건짜리면 붙이지 않는다.</summary>
    private static string Partial(int done, int total)
        => done > 0 && total > 1 ? $"{total}건 중 {done}건까지 처리한 뒤 멈췄습니다. 나머지만 다시 고르세요.\n" : "";

    private async Task<int> ProcessIdAsync(int operCode, CancellationToken ct)
        => (await _processes.GetAllAsync(ct)).FirstOrDefault(p => p.OperCode == operCode)?.ProcessDefinitionId ?? 0;

    private async Task<OperLotItemDto?> FindLotAsync(int operCode, int lotId, CancellationToken ct)
    {
        var processDefinitionId = await ProcessIdAsync(operCode, ct);
        var lots = await _query.SearchAsync(new OperLotSearchRequest(processDefinitionId), ct);
        return lots.FirstOrDefault(l => l.LotId == lotId);
    }

    private static SaveInspectionPanelRequest ToSaveRequest(MesOperSaveRequest r)
        => ToSaveRequest(r.LotId, r.RecipeDefinitionId, r.ResId, r.CmtAets, r.Inputs);

    private static SaveInspectionPanelRequest ToSaveRequest(MesOperExecuteRequest r)
        => ToSaveRequest(r.LotId, r.RecipeDefinitionId, r.ResId, r.CmtAets, r.Inputs);

    private static SaveInspectionPanelRequest ToSaveRequest(
        int lotId, int? recipeDefinitionId, string? resId, string? cmtAets, IReadOnlyList<MesInspInputDto> inputs)
        => new(lotId, recipeDefinitionId, resId, cmtAets,
            inputs.Select(i => new InspectionParameterInputDto(i.ParameterDefinitionId, i.InputValue, i.Comment)).ToList());

    private static string? ValueOf(IReadOnlyList<MesInspInputDto> inputs, int parameterDefinitionId)
        => inputs.FirstOrDefault(i => i.ParameterDefinitionId == parameterDefinitionId)?.InputValue;
}

/// <summary>
/// OPER 검사값 표를 화면이 쓰는 모양으로 바꾸고 SPEC 을 판정한다.
///
/// 컨트롤러에서 꺼내 둔 이유는 하나다 — <b>테스트할 수 있어야 하기 때문</b>이다.
/// 어떤 입력칸을 그릴지(Y/N · OK/NG/CC · 다측정 · 단일)와 SPEC OUT 판정은 작업자가 잘못된 값을
/// 그대로 넘기지 못하게 막는 자리라, 규칙이 바뀌었을 때 조용히 어긋나면 안 된다.
/// 규칙 자체는 MES 데스크톱판과 공유하는 Domain 의 <see cref="InspectionValueRules"/> 가 원본이다.
/// </summary>
public static class MesInspectionRows
{
    /// <summary>검사값 표 한 행 — 어떤 입력칸을 그릴지까지 정해서 내려준다.</summary>
    public static MesInspRowDto ToRow(InspectionParameterRowDto r)
    {
        var count = InspectionValueRules.NormalizeValueCount(r.ValueCount);
        var multi = InspectionValueRules.IsMultiPoint(r.ParameterType, r.Code, r.Description, r.ValueCount);

        // 다측정은 저장값('A|B|C')을 그대로 두고 칸별로 쪼개 보여 준다.
        // 단일은 미측정 기본값 규칙을 태운다(외관 Y/N 은 비어 있으면 N).
        var value = multi
            ? r.InputValue
            : InspectionValueRules.DefaultInputValue(r.ParameterType, r.InputValue);

        var points = multi
            ? InspectionValueRules.SplitPoints(r.InputValue, count)
                .Select((v, i) => new MesInspPointDto(InspectionValueRules.PointLabels[i].ToString(), v))
                .ToList()
            : new List<MesInspPointDto>();

        return new MesInspRowDto(
            r.ParameterDefinitionId, r.Code, r.Description, r.MinValue, r.MaxValue,
            r.ReferenceInputValue, value, r.Comment,
            InspectionValueRules.IsYesNo(r.ParameterType),
            InspectionValueRules.IsOkNgCc(r.ParameterType),
            multi,
            points);
    }

    /// <summary>
    /// 편집 가능한 표(2100=IN INSP, 7000=FI INSP)의 값만 SPEC 판정한다 — 저장 대상과 같은 범위다.
    /// 읽기전용으로 보여 주는 표까지 판정하면, 고칠 수도 없는 값 때문에 실행이 막힌다.
    /// </summary>
    public static IReadOnlyList<MesSpecOutDto> SpecOut(
        InspectionPanelDto panel, IReadOnlyList<MesInspInputDto> inputs)
    {
        var rows = panel.ShowsFiInsp
            ? panel.FiInspRows
            : (panel.InInspEditable
                ? panel.InInspRows
                : (IReadOnlyList<InspectionParameterRowDto>)Array.Empty<InspectionParameterRowDto>());

        var found = new List<MesSpecOutDto>();
        foreach (var row in rows)
        {
            var value = inputs.FirstOrDefault(i => i.ParameterDefinitionId == row.ParameterDefinitionId)?.InputValue;
            var reason = SpecOutReason(row, value);
            if (reason is not null) found.Add(new MesSpecOutDto(row.ParameterDefinitionId, reason));
        }
        return found;
    }

    /// <summary>한 행의 SPEC 위반 사유. 정상이거나 미측정이면 null.</summary>
    public static string? SpecOutReason(InspectionParameterRowDto row, string? value)
    {
        var count = InspectionValueRules.NormalizeValueCount(row.ValueCount);
        var values = InspectionValueRules.IsMultiPoint(row.ParameterType, row.Code, row.Description, row.ValueCount)
            ? InspectionValueRules.SplitPoints(value, count)
                .Select((v, i) => ((string?)InspectionValueRules.PointLabels[i].ToString(), v))
            : new (string?, string?)[] { (null, value) };

        return InspectionValueRules.GetSpecOutReason(
            row.ParameterType, row.Code, row.Description, row.MinValue, row.MaxValue, values);
    }
}

// ── DTO ────────────────────────────────────────────────────────────────────

/// <summary><paramref name="ScreenName"/> 은 사이드바 메뉴와 같은 이름(없으면 빈 문자열).</summary>
public record MesOperScreenDto(
    int OperCode,
    int ProcessDefinitionId,
    string OperName,
    string ScreenName,
    bool IsRecipeOper,
    bool RequiresRecipeAndEquipment,
    bool SupportsMultiSelect);

public record MesInspPointDto(string Label, string? Value);

/// <summary>
/// <paramref name="IsMultiPoint"/> 면 <paramref name="Points"/> 를 A~E 칸으로 그리고,
/// <paramref name="IsYesNo"/> / <paramref name="IsOkNgCc"/> 면 각각 Y·N / OK·NG·CC 중에서 고르게 한다.
/// 셋 다 아니면 그냥 한 칸이다.
/// </summary>
public record MesInspRowDto(
    int ParameterDefinitionId,
    string Code,
    string Description,
    decimal? MinValue,
    decimal? MaxValue,
    string? ReferenceInputValue,
    string? InputValue,
    string? Comment,
    bool IsYesNo,
    bool IsOkNgCc,
    bool IsMultiPoint,
    IReadOnlyList<MesInspPointDto> Points);

public record MesRecipeOptionDto(int RecipeDefinitionId, string RecipeDescription, int? ReadTimeMinutes);

/// <summary><paramref name="RequiresReasonCode"/>: 이 TRAN 을 고르면 사유 코드를 받아야 한다.</summary>
public record MesTranOptionDto(int TransitionId, string Description, bool RequiresReasonCode);

public record MesBatchMemberDto(string LotNumber, string SerialNumber);

public record MesOperPanelDto(
    int LotId,
    string LotNumber,
    string MatId,
    string MatDesc,
    string Sn,
    int ClnCount,
    int? RecipeDefinitionId,
    string? PmResId,
    string? ResId,
    string? CmtAets,
    string? CommentHistory,
    bool ShowsInInsp,
    bool InInspEditable,
    bool ShowsFiInsp,
    IReadOnlyList<MesInspRowDto> InInspRows,
    IReadOnlyList<MesInspRowDto> FiInspRows,
    IReadOnlyList<MesRecipeOptionDto> Recipes,
    IReadOnlyList<MesTranOptionDto> Transitions,
    IReadOnlyList<MesBatchMemberDto> BatchMembers);

public record MesInspInputDto(int ParameterDefinitionId, string? InputValue, string? Comment);

public record MesOperSaveRequest(
    int LotId,
    int? RecipeDefinitionId,
    string? ResId,
    string? CmtAets,
    IReadOnlyList<MesInspInputDto> Inputs);

/// <summary>
/// <paramref name="LotId"/> 는 화면 아래 패널에 열려 있는 LOT — 검사값·레시피·코멘트는 이 LOT 에 저장된다.
/// <paramref name="LotIds"/> 는 실제로 옮길 LOT 들이다(다중선택 공정이면 여럿). 첫 번째가 패널의 LOT 이다.
/// <paramref name="Confirmed"/> 는 확인 창에서 [그대로 진행] 을 누른 뒤의 재시도.
/// </summary>
public record MesOperExecuteRequest(
    int LotId,
    int? RecipeDefinitionId,
    string? ResId,
    string? CmtAets,
    IReadOnlyList<MesInspInputDto> Inputs,
    IReadOnlyList<int> LotIds,
    int TransitionId,
    string? ReasonCode,
    bool Confirmed);

public record MesOperAdvanceRequest(IReadOnlyList<int> LotIds, int TransitionId, string? ReasonCode);

public record MesSpecOutDto(int ParameterDefinitionId, string Reason);

/// <summary><paramref name="Outcome"/>: blocked · needsConfirm · openOutput · done.</summary>
public record MesOperExecuteResultDto(
    string Outcome,
    string Message,
    int? OutputLotId = null,
    string? OutputLotNumber = null)
{
    public static MesOperExecuteResultDto Blocked(string message) => new("blocked", message);
    public static MesOperExecuteResultDto NeedsConfirm(string message) => new("needsConfirm", message);
    public static MesOperExecuteResultDto Done(string message) => new("done", message);
    public static MesOperExecuteResultDto OpenOutput(int lotId, string lotNumber, string message)
        => new("openOutput", message, lotId, lotNumber);
}
