using Microsoft.EntityFrameworkCore;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Infrastructure.Data;

/// <summary>
/// MES 기준 데이터 — 공정 10단계 · 공정 플로우 · TRAN 전이 정의 · 사유코드 · LINE · 레시피 정의 ·
/// 파라미터 카탈로그.
///
/// <b>이것이 없으면 MES 는 아무 일도 할 수 없다.</b> 그런데 셋업 화면으로는 만들 수 없는 것들이다 —
/// 공정의 OPER 코드, TRAN 전이 정의, 사유코드, LINE, 레시피 정의에는 편집 화면이 아예 없다.
/// 그래서 개발용 샘플(가짜 업체·제품·LOT)과 달리 <b>환경을 가리지 않고 항상</b> 넣는다.
///
/// 여러 번 떠도 중복되지 않게, 이미 들어 있으면 지나간다. 다만 TRAN 전이 정의는 나중에 행이
/// 늘어날 수 있어 없는 것만 채워 넣는다(멱등).
/// </summary>
public static class MesBaseDataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext context, CancellationToken cancellationToken = default)
    {
        // 공정 정의가 있으면 이미 한 번 깔린 DB 다. 그 뒤에 늘어난 TRAN 행만 채우고 나간다.
        if (await context.ProcessDefinitions.AnyAsync(cancellationToken))
        {
            await EnsureTransitionDefinitionsAsync(context, cancellationToken);
            return;
        }

        var now = DateTime.Now;
        context.SystemSequences.Add(new SystemSequence { SequenceName = "LotNumber", CurrentValue = 0 });

        context.SystemSettings.AddRange(
            new SystemSetting { SettingKey = "LongWait.Cleaning.WarningHours", SettingValue = "4", Description = "세정 공정 주의 기준시간(시간)" },
            new SystemSetting { SettingKey = "LongWait.Cleaning.DelayHours", SettingValue = "8", Description = "세정 공정 지연 기준시간(시간)" });

        // Phase 14 OPER 10단계 코드 체계. 6000대는 향후 확장을 위해 의도적으로 비워둔다.
        var registration = new ProcessDefinition { ProcessCode = "REGISTRATION", ProcessName = "전산등록", OperCode = 1000 };
        var receiving = new ProcessDefinition { ProcessCode = "RECEIVING", ProcessName = "입고", OperCode = 2000 };
        var incomingInspection = new ProcessDefinition { ProcessCode = "INCOMING_INSPECTION", ProcessName = "입고검사", OperCode = 2100 };
        var cleaning = new ProcessDefinition { ProcessCode = "CLEANING", ProcessName = "세정", OperCode = 3000 };
        var drying = new ProcessDefinition { ProcessCode = "DRYING", ProcessName = "건조", OperCode = 4000 };
        var laserCo2 = new ProcessDefinition { ProcessCode = "LASER_CO2", ProcessName = "Laser&CO2", OperCode = 4100 };
        var bake = new ProcessDefinition { ProcessCode = "BAKE", ProcessName = "Bake", OperCode = 5000 };
        var outgoingInspection = new ProcessDefinition { ProcessCode = "OUTGOING_INSPECTION", ProcessName = "출고검사", OperCode = 7000 };
        var packaging = new ProcessDefinition { ProcessCode = "PACKAGING", ProcessName = "포장완료", OperCode = 7100 };
        var shipping = new ProcessDefinition { ProcessCode = "SHIPPING", ProcessName = "고객출하", OperCode = 8100 };
        context.ProcessDefinitions.AddRange(
            registration, receiving, incomingInspection, cleaning, drying, laserCo2, bake,
            outgoingInspection, packaging, shipping);

        // STANDARD: 10단계 전체 - 플로우 미지정 시 기본값(RegistrationViewModel이 항상 이 코드를 기본
        // 선택). SIMPLE은 2026-08-24 피드백으로 제거 - 관리자가 "공정 관리" 탭에서 플로우를 직접
        // 추가/편집할 수 있게 된 이후로는 더 이상 필요 없는 임시 시작점이었다.
        var standardRoute = new ProcessRoute { RouteCode = "STANDARD", RouteName = "표준 경로" };
        context.ProcessRoutes.Add(standardRoute);

        void AddRouteSteps(ProcessRoute route, params ProcessDefinition[] steps)
        {
            for (var i = 0; i < steps.Length; i++)
            {
                context.ProcessRouteSteps.Add(new ProcessRouteStep { ProcessRoute = route, ProcessDefinition = steps[i], StepOrder = i + 1 });
            }
        }

        AddRouteSteps(standardRoute, registration, receiving, incomingInspection, cleaning, drying, laserCo2, bake, outgoingInspection, packaging, shipping);

        // "품목 공정 플로우 설정" 화면이 참조하는 이름 붙은 플로우 - 2026-08-24 피드백으로 F0001~F0005만
        // 실제 사용하는 표준 예외 플로우 세트로 확정됐다(STANDARD는 등록 화면의 기술적 기본값으로만 남고,
        // 실제 제품에 배정하는 건 전부 이 5개). TRAN 마스터 데이터(T570~T610)를 보면 LASER(4100)를 거치는
        // 모든 경로는 반드시 "건조→LASER→세정→건조"로 세정/건조를 한 번 더 반복한다(T590: 건조 완료→LASER,
        // T580: LASER 완료→세정로 되돌아감) - 그래서 LASER를 포함하는 F0004/F0005는 세정/건조가 두 번씩
        // 등장한다. (ProcessRouteId, StepOrder) unique 인덱스만 있고 ProcessDefinitionId 자체의 중복은
        // 막지 않으므로 같은 공정을 순서만 다르게 여러 번 넣는 것은 스키마상 문제없다.
        var flowF0001 = new ProcessRoute { RouteCode = "F0001", RouteName = "세정+건조 (열처리 포함)" };
        var flowF0002 = new ProcessRoute { RouteCode = "F0002", RouteName = "세정+건조 (열처리 미포함)" };
        var flowF0003 = new ProcessRoute { RouteCode = "F0003", RouteName = "공정 미진행 (2000→7000)" };
        var flowF0004 = new ProcessRoute { RouteCode = "F0004", RouteName = "세정+건조 (LASER 포함, 열처리 제외)" };
        var flowF0005 = new ProcessRoute { RouteCode = "F0005", RouteName = "세정+건조 (LASER, 열처리 포함)" };
        context.ProcessRoutes.AddRange(flowF0001, flowF0002, flowF0003, flowF0004, flowF0005);

        AddRouteSteps(flowF0001, registration, receiving, incomingInspection, cleaning, drying, bake, outgoingInspection, packaging, shipping);
        AddRouteSteps(flowF0002, registration, receiving, incomingInspection, cleaning, drying, outgoingInspection, packaging, shipping);
        AddRouteSteps(flowF0003, registration, receiving, outgoingInspection, packaging, shipping);
        AddRouteSteps(flowF0004, registration, receiving, incomingInspection, cleaning, drying, laserCo2, cleaning, drying, outgoingInspection, packaging, shipping);
        AddRouteSteps(flowF0005, registration, receiving, incomingInspection, cleaning, drying, laserCo2, cleaning, drying, bake, outgoingInspection, packaging, shipping);

        // TRAN CODE 전이 엔진 Master Data. "기타프로그램 마스터 데이터.xlsx" TRAN 사용 공정 시트를 그대로 옮긴
        // 것으로, 사용자가 직접 편집(2026-08-18)한 최신본 46행이다. 6000(수리)/8000(창고이동) OPER은
        // 사용자 지시로 이번 범위에서 제외했고, 이 46행 어디에도 두 OPER은 등장하지 않는다.
        // T530(SHIP)만 TargetOperCode가 null - 8100 고객출하에서 더 넘어갈 다음 OPER이 없는 종결 처리.
        var tranRows = GetTransitionRows();
        context.ProcessTransitionDefinitions.AddRange(tranRows.Select(r => new ProcessTransitionDefinition
        {
            TranId = r.TranId,
            Description = r.Description,
            SourceOperCode = r.SourceOperCode,
            TranCode = r.TranCode,
            TargetOperCode = r.TargetOperCode
        }));

        // HOLD/RELEASE/재작업/SKIP/SHIP 정의 시트 5개.
        var reasonRows = new (ReasonCategory Category, string Code, string Description)[]
        {
            (ReasonCategory.Hold, "ETC", "기타사유"),
            (ReasonCategory.Hold, "SPEC", "품질데이터 이상"),
            (ReasonCategory.Hold, "STOCK", "장기재고"),
            (ReasonCategory.Hold, "VISUAL", "외관 이상"),
            (ReasonCategory.Hold, "BROKEN", "파손"),
            (ReasonCategory.Release, "AGREE", "작업진행 승인"),
            (ReasonCategory.Release, "ETC", "기타사유"),
            (ReasonCategory.Rework, "BROKEN", "파손"),
            (ReasonCategory.Rework, "ETC", "기타사유"),
            (ReasonCategory.Rework, "LASER", "레이저 완료"),
            (ReasonCategory.Rework, "STAIN", "얼룩 잔존"),
            (ReasonCategory.Rework, "UNETCH", "막질 잔존"),
            (ReasonCategory.Skip, "ETC", "기타사유"),
            (ReasonCategory.Skip, "IN_REJECT", "반입부적합"),
            (ReasonCategory.Skip, "REJECT", "사내 Broken"),
            (ReasonCategory.Ship, "CUST", "고객 출하"),
        };
        context.ReasonCodes.AddRange(reasonRows.Select(r => new ReasonCode
        {
            Category = r.Category,
            Code = r.Code,
            Description = r.Description
        }));

        // "기타프로그램 마스터 데이터.xlsx"의 고객사 LINE 정의/파라미터 정의/레시피 정의 시트 Master Data
        // (2026-08-18). 실제 고객사명이 아니라 사내에서 쓰는 LINE 코드/사용자 계정 코드라 CLAUDE.md 11번
        // (실제 고객사명 개발데이터 금지) 대상이 아니다 - "고객사 정의" 시트(64개 실제 회사명)와는 다르다.
        var lineRows = new (string Code, string Description, string UserCode, int SortOrder)[]
        {
            ("제우스", "제우스", "IZEUS", 21),
            ("진성", "진성큐엔에스", "I진성", 23),
            ("ADEK", "어드벤스일렉트릭코리아", "IADEK", 17),
            ("DST", "DS테크노", "IDST", 10),
            ("EUGENE", "유진테크", "IEUGENE", 18),
            ("KEK", "국제엘렉트릭코리아", "IKEK", 22),
            ("KKQ", "금강쿼츠", "IGKQ", 14),
            ("OTHER", "Other", "IOTHER", 19),
            ("SEM", "세메스", "ISEM", 12),
            ("TRS", "원익IPS", "ITRS", 16),
            ("TTS", "티티에스", "ITTS", 15),
            ("WOOAM", "우암신소재", "IWOOAM", 11),
            ("YSQ", "영신쿼츠", "IYSQ", 13),
        };
        var lineDefinitions = lineRows.Select(r => new LineDefinition
        {
            Code = r.Code,
            Description = r.Description,
            UserCode = r.UserCode,
            SortOrder = r.SortOrder
        }).ToList();
        context.LineDefinitions.AddRange(lineDefinitions);

        // 2026-08-26: 제품별 파라미터(ProductId 있는 행)는 "기본 빈 상태"로 둔다(피드백: "파라미터는
        // 기본적으로는 아무 데이터 없는 상태로... 제품별로 사용 할 파라미터를 추가"). 다만 편집 폼의
        // PARAMETER 드롭다운이 고를 수 있도록 "카탈로그"(ProductId=NULL, 코드+설명 참조행)만 시드한다.
        // 사용자는 제품을 선택하고 이 카탈로그에서 코드를 골라 제품별 파라미터를 추가하거나, COPY 탭으로
        // 다른 세정코드에서 통째로 불러온다.
        var catalogRows = new (string Code, string Description, ParameterType Type)[]
        {
            ("CHIP", "깨짐", ParameterType.Boolean),
            ("CRACK", "CRACK", ParameterType.Boolean),
            ("SCR", "긁힘", ParameterType.Boolean),
            ("STAIN", "얼룩", ParameterType.Boolean),
            ("PIT", "패임", ParameterType.Boolean),
            ("STRIP", "벗겨짐", ParameterType.Boolean),
            ("UNETCH", "UNETCH", ParameterType.Boolean),
            ("VISUAL", "외관불량", ParameterType.Boolean),
            ("HOLE", "HOLE", ParameterType.Boolean),
            ("RESULT", "합부판정", ParameterType.Choice),
            ("WEIGHT", "무게(g)", ParameterType.Numeric),
            ("THK", "두께(㎜)", ParameterType.Numeric),
            ("PARTICLE", "표면먼지(ea/sq.in)", ParameterType.Numeric),
            ("ROUGH", "표면거칠기(㎛)", ParameterType.Numeric),
            ("NTHK", "누적막두께(Å)", ParameterType.Numeric),
            ("HOLE SIZE", "HOLE(r=mm)", ParameterType.Numeric),
            ("DIMEN1", "내측 직경", ParameterType.Numeric),
            ("DIMEN2", "내측 마모", ParameterType.Numeric),
            ("DIMEN3", "PAD 높이", ParameterType.Numeric),
            ("DIMEN4", "전체 높이", ParameterType.Numeric),
            ("FFLAT", "전면 평탄도", ParameterType.Numeric),
            ("BFLAT", "후면 평탄도", ParameterType.Numeric),
            ("CFLAT", "Helicoil 깊이", ParameterType.Numeric),
        };
        var catalogSort = 0;
        context.ParameterDefinitions.AddRange(catalogRows.Select(r => new ParameterDefinition
        {
            Code = r.Code,
            Description = r.Description,
            ParameterType = r.Type,
            SortOrder = ++catalogSort,
            Oper = null,
            ValueCount = 1,
            Unit = null,
            MinValue = null,
            MaxValue = null,
            IsActive = true,
            ProductId = null   // 카탈로그(참조) 행 - 어떤 제품에도 배정되지 않음.
        }));

        // "레시피 정의" 시트(48행) 원본 그대로 - RECIPE ID/RECIPE DESC/READ TIME (Min)/OPER
        // (2026-08-24: OPER별 필터링 + READ TIME 신설을 위해 실제 마스터 데이터로 교체).
        var recipeRows = new (string Code, string Description, int OperCode, int? ReadTimeMinutes)[]
        {
            ("A000", "0+15+100", 3000, 115),
            ("A005", "5+15+100", 3000, 120),
            ("A100", "0+5+100", 3000, 120),
            ("BAKE6.5", "6.5시간 열처리(600도)", 5000, 390),
            ("BAKE7.5", "7.5시간 열처리(700도)", 5000, 450),
            ("BAKE8", "8시간 열처리(750도)", 5000, 480),
            ("DRY 30M", "30분 건조(DRY OVEN)", 5000, 30),
            ("DRY 4H", "4시간 건조 (DRY OVEN)", 4000, 240),
            ("DRY 6H", "6시간 건조 (QTZ OVEN)", 4000, 360),
            ("DRY 8H", "8시간 건조 (DRY OVRN)", 4000, 480),
            ("DRY OVEN 150", "150분 건조", 5000, 150),
            ("DRY12", "12시간 건조", 4000, 720),
            ("DRY24", "24시간 건조", 4000, 1440),
            ("DRY36", "36시간 건조", 4000, 2160),
            ("DRY48", "48시간 건조", 4000, 2880),
            ("R000", "0+30+100", 3000, 130),
            ("R001", "1+30+100", 3000, 131),
            ("R005", "5+30+100", 3000, 135),
            ("R010", "10+30+100", 3000, 140),
            ("R011", "100+100 (P20  65℃ 60분)", 3000, 200),
            ("R015", "15+30+100", 3000, 145),
            ("R016", "15+10+100", 3000, 125),
            ("R020", "20+10+100", 3000, 130),
            ("R025", "25+30+100", 3000, 155),
            ("R030", "30+30+100", 3000, 160),
            ("R035", "120+30+100 (S2 60℃ 120분)", 3000, 250),
            ("R045", "45+30+100", 3000, 175),
            ("R060", "60+30+100", 3000, 190),
            ("R075", "75+30+100", 3000, 205),
            ("R100", "0+0+100", 3000, 100),
            ("R150", "5+0+60", 3000, 65),
            ("R160", "60+30+100 (S2 60℃ 60분)", 3000, 190),
            ("R170", "210+30+100 (S2 60℃ 120분)", 3000, 340),
            ("R180", "180+30+100", 3000, 310),
            ("R200", "200+30+100", 3000, 330),
            ("R260", "60+60+100", 3000, 220),
            ("R300", "300+30+100", 3000, 430),
            ("R610", "10+10+100", 3000, 120),
            ("R620", "20+10+100", 3000, 130),
            ("R621", "20+20+100", 3000, 140),
            ("R630", "30+10+100", 3000, 140),
            ("R645", "45+10+100", 3000, 155),
            ("R660", "150+30+100 (S2 60℃ 150분)", 3000, 280),
            ("R670", "180+30+100 (S2 60℃ 150분)", 3000, 310),
            ("R680", "90+10+100 (S2 60℃ 90분)", 3000, 200),
            ("R690", "120+3 (Alkali 60℃ 120분, DI 40℃ 3분)", 3000, 123),
            ("R700", "30+30+100 (S2 60℃ 30분)", 3000, 160),
            ("R710", "0+0+10 (DI 40℃)", 3000, 10),
        };
        context.RecipeDefinitions.AddRange(recipeRows.Select(r => new RecipeDefinition
        {
            Code = r.Code,
            Description = r.Description,
            OperCode = r.OperCode,
            ReadTimeMinutes = r.ReadTimeMinutes
        }));
        await context.SaveChangesAsync(cancellationToken);
    }

    // 2026-08-26: "기타프로그램 마스터 데이터.xlsx" TRAN 사용 공정 시트(46행)의 원본 전이 정의. 신규 시드와
    // 기존 DB 보충(EnsureTransitionDefinitionsAsync)이 같은 데이터를 쓰도록 정적 메서드로 뺐다.
    private static (string TranId, string Description, int SourceOperCode, TranCode TranCode, int? TargetOperCode)[] GetTransitionRows() => new[]
    {
        ("T040", "입고검사 진행", 2000, TranCode.End, (int?)2100),
        ("T060", "불량 이동", 2000, TranCode.Skip, 7000),
        ("T065", "출하검사 이동(장기재고)", 2000, TranCode.End, 7000),
        ("T070", "작업 중지(HOLD)", 2000, TranCode.Hold, 2000),
        ("T080", "작업 중지 해제", 2000, TranCode.Release, 2000),
        ("T085", "장기재고이동", 2000, TranCode.End, 8100),
        ("T090", "입고검사 완료", 2100, TranCode.End, 3000),
        ("T110", "불량 이동", 2100, TranCode.Skip, 7000),
        ("T120", "작업 중지", 2100, TranCode.Hold, 2100),
        ("T130", "작업 중지 해제", 2100, TranCode.Release, 2100),
        ("T140", "세정 시작", 3000, TranCode.Start, 3000),
        ("T150", "세정 완료", 3000, TranCode.End, 4000),
        ("T160", "재세정 의뢰", 3000, TranCode.Rework, 3000),
        ("T180", "불량 이동", 3000, TranCode.Skip, 7000),
        ("T190", "작업 중지", 3000, TranCode.Hold, 3000),
        ("T200", "작업 중지 해제", 3000, TranCode.Release, 3000),
        ("T210", "건조 시작", 4000, TranCode.Start, 4000),
        ("T220", "건조 완료", 4000, TranCode.End, 7000),
        ("T620", "건조 완료 (열처리)", 4000, TranCode.End, 5000),
        ("T230", "재세정 의뢰", 4000, TranCode.Rework, 3000),
        ("T250", "불량 이동", 4000, TranCode.Skip, 7000),
        ("T260", "작업 중지", 4000, TranCode.Hold, 4000),
        ("T270", "작업 중지 해제", 4000, TranCode.Release, 4000),
        ("T280", "열처리 시작", 5000, TranCode.Start, 5000),
        ("T290", "열처리 완료", 5000, TranCode.End, 7000),
        ("T300", "재세정 의뢰", 5000, TranCode.Rework, 3000),
        ("T320", "불량 이동", 5000, TranCode.Skip, 7000),
        ("T330", "작업 중지", 5000, TranCode.Hold, 5000),
        ("T340", "작업 중지 해제", 5000, TranCode.Release, 5000),
        ("T390", "출하검사 완료", 7000, TranCode.End, 7100),
        ("T400", "재세정의뢰", 7000, TranCode.Rework, 3000),
        ("T430", "작업 중지", 7000, TranCode.Hold, 7000),
        ("T440", "작업 중지 해제", 7000, TranCode.Release, 7000),
        ("T455", "포장 완료", 7100, TranCode.End, 8100),
        ("T460", "작업 중지", 7100, TranCode.Hold, 7100),
        ("T470", "작업 중지 해제", 7100, TranCode.Release, 7100),
        ("T485", "재세정의뢰", 7100, TranCode.Rework, 3000),
        ("T530", "고객 출하", 8100, TranCode.Ship, null),
        ("T535", "천안 세정 이동", 8100, TranCode.End, 2000),
        ("T540", "작업 중지", 8100, TranCode.Hold, 8100),
        ("T550", "작업 중지 해제", 8100, TranCode.Release, 8100),
        ("T570", "LASER 시작", 4100, TranCode.Start, 4100),
        ("T580", "LASER 완료", 4100, TranCode.End, 3000),
        ("T590", "LASER 이동", 4000, TranCode.End, 4100),
        ("T600", "출하검사 이동(LASER)", 4000, TranCode.End, 7000),
        ("T610", "건조 완료", 4000, TranCode.End, 5000),
    };

    // 2026-08-26 피드백: 기존 DB에 마스터 전이 정의가 일부만 있어 OPER별 TRAN이 다 안 보이는 문제 보정.
    // TranId 기준으로 없는 행만 추가한다(멱등). 기존 행의 내용은 건드리지 않는다.
    internal static async Task EnsureTransitionDefinitionsAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var existing = (await context.ProcessTransitionDefinitions.Select(t => t.TranId).ToListAsync(cancellationToken)).ToHashSet();
        var missing = GetTransitionRows().Where(r => !existing.Contains(r.TranId)).ToList();
        if (missing.Count == 0) { return; }
        context.ProcessTransitionDefinitions.AddRange(missing.Select(r => new ProcessTransitionDefinition
        {
            TranId = r.TranId,
            Description = r.Description,
            SourceOperCode = r.SourceOperCode,
            TranCode = r.TranCode,
            TargetOperCode = r.TargetOperCode,
            IsActive = true
        }));
        await context.SaveChangesAsync(cancellationToken);
    }
}
