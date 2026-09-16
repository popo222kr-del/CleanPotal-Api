using Microsoft.EntityFrameworkCore;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;
using ProductionManagement.Infrastructure.Sequencing;

namespace ProductionManagement.Infrastructure.Data;

// 개발 환경 전용 Sample Data. 실제 고객사명/제품/성적서는 절대 사용하지 않는다 (CLAUDE.md 11번/절대 금지사항).
// Customers가 하나라도 있으면 이미 Seed된 것으로 보고 다시 실행하지 않는다 (앱을 여러 번 켜도 중복 삽입 방지).
public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        ILotNumberGenerator lotNumberGenerator,
        ICurrentUserProvider currentUserProvider,
        CancellationToken cancellationToken = default)
    {
        // 2026-08-28: 최초 관리자(로그인 계정) 시드는 IAuthService.EnsureDefaultAdminAsync가 운영/개발 공통으로
        // 앱 시작 시 처리한다(여기서의 Windows 계정 부트스트랩은 폐지). currentUserProvider는 시그니처 호환용.
        _ = currentUserProvider;

        if (await context.Customers.AnyAsync(cancellationToken))
        {
            // 기존 DB(이미 시딩됨)에도 제품별 2100/7000 검사 파라미터가 없으면 보충한다(2026-08-26 피드백).
            await EnsureProductInspectionParametersAsync(context, cancellationToken);
            // 기존 DB에 마스터 전이 정의가 일부만 있으면 보충한다(OPER별 TRAN 누락 방지, 2026-08-26 피드백).
            await EnsureTransitionDefinitionsAsync(context, cancellationToken);
            // 기존 DB에 BROKEN/ETC(외관 Boolean)가 없으면 제품별로 보충한다(2026-08-27 피드백).
            await EnsureExtraVisualParametersAsync(context, cancellationToken);
            // 기존 파라미터에 성적서 표기명(CertificateLabel)이 비어 있으면 코드로 보충한다(2026-08-27 피드백).
            await EnsureParameterCertificateLabelsAsync(context, cancellationToken);
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

        // 실제 고객사명은 사용하지 않는다(CLAUDE.md 11번, 2026-08-18 재확인) - "고객사 정의" 시트의 실제
        // 64개 회사명 대신, 업체 Dropdown이 너무 휑해 보이지 않도록 가짜지만 그럴듯한 이름 몇 개를 추가한다.
        // LineDefinition(업체가 속한 LINE 대분류)을 몇 곳에 배정해 데모로 보여준다 - "고객사 정의" 시트처럼
        // 여러 업체(소분류)가 같은 LINE 아래 묶일 수 있다는 걸 보여주려고 A/B사는 같은 LINE(SEM)으로 뒀다
        // (2026-08-18 피드백: "LINE - 업체 이렇게 생각해주면 돼").
        var lineSem = lineDefinitions.First(l => l.Code == "SEM");
        var lineOther = lineDefinitions.First(l => l.Code == "OTHER");
        var customerA = new Customer { CustomerCode = "CUST-A", CustomerName = "A사", ExportPrefix = "AA", LineDefinition = lineSem, CreatedAt = now, UpdatedAt = now };
        var customerB = new Customer { CustomerCode = "CUST-B", CustomerName = "B사", ExportPrefix = "BB", LineDefinition = lineSem, CreatedAt = now, UpdatedAt = now };
        var customerC = new Customer { CustomerCode = "CUST-C", CustomerName = "C사", ExportPrefix = "CC", LineDefinition = lineOther, CreatedAt = now, UpdatedAt = now };
        var customerD = new Customer { CustomerCode = "CUST-D", CustomerName = "디자인반도체", ExportPrefix = "DD", CreatedAt = now, UpdatedAt = now };
        var customerE = new Customer { CustomerCode = "CUST-E", CustomerName = "이엔지쿼츠", ExportPrefix = "EE", CreatedAt = now, UpdatedAt = now };
        var customerF = new Customer { CustomerCode = "CUST-F", CustomerName = "에프텍전자", ExportPrefix = "FF", CreatedAt = now, UpdatedAt = now };
        context.Customers.AddRange(customerA, customerB, customerC, customerD, customerE, customerF);

        // "고객사 정의" 시트의 실제 구조(62개 업체, LINE별 분포 - SEM 48/OTHER 6/그 외 LINE당 1개씩 +
        // 미배정 1개)를 그대로 반영하되 회사명만 가짜로 바꿨다(2026-08-18 피드백: "업체 DATA는 CUSTOMER
        // 정의 SHEET를 읽어주면 돼" + AskUserQuestion 답변 "여전히 가짜 이름으로"). 위 A/B/C사가 이미
        // SEM 2개+OTHER 1개를 차지하고 있어 여기서는 나머지(SEM 46/OTHER 5 + LINE당 1개씩)만 채운다.
        var extraCustomerRows = new (string Code, string Name, string ExportPrefix, string LineCode)[]
        {
            ("CUST-GA", "경신코리아", "GA", "SEM"), ("CUST-GB", "해솔시스템즈", "GB", "SEM"),
            ("CUST-GC", "경신반도체", "GC", "SEM"), ("CUST-GD", "대신산업", "GD", "SEM"),
            ("CUST-GE", "금강머티리얼즈", "GE", "SEM"), ("CUST-GF", "광성정공", "GF", "SEM"),
            ("CUST-GG", "프라임시스템즈", "GG", "SEM"), ("CUST-GH", "동현엔지니어링", "GH", "SEM"),
            ("CUST-GI", "스카이정공", "GI", "SEM"), ("CUST-GJ", "베스트화학", "GJ", "SEM"),
            ("CUST-GK", "오르비화학", "GK", "SEM"), ("CUST-GL", "경일반도체", "GL", "SEM"),
            ("CUST-GM", "해솔정공", "GM", "SEM"), ("CUST-GN", "해솔반도체", "GN", "SEM"),
            ("CUST-GO", "태평양엔지니어링", "GO", "SEM"), ("CUST-GP", "청우소재", "GP", "SEM"),
            ("CUST-GQ", "광성엔지니어링", "GQ", "SEM"), ("CUST-GR", "성일코리아", "GR", "SEM"),
            ("CUST-GS", "베스트정밀", "GS", "SEM"), ("CUST-GT", "태평양정밀", "GT", "SEM"),
            ("CUST-GU", "한창전자", "GU", "SEM"), ("CUST-GV", "실버소재", "GV", "SEM"),
            ("CUST-GW", "경신테크머티리얼즈", "GW", "SEM"), ("CUST-GX", "경성코리아", "GX", "SEM"),
            ("CUST-GY", "대성전자", "GY", "SEM"), ("CUST-GZ", "블루오션엔지니어링", "GZ", "SEM"),
            ("CUST-HA", "대신머티리얼즈", "HA", "SEM"), ("CUST-HB", "성신산업", "HB", "SEM"),
            ("CUST-HC", "동진시스템즈", "HC", "SEM"), ("CUST-HD", "청우전자", "HD", "SEM"),
            ("CUST-HE", "우창코리아", "HE", "SEM"), ("CUST-HF", "한마루소재", "HF", "SEM"),
            ("CUST-HG", "블루오션머티리얼즈", "HG", "SEM"), ("CUST-HH", "한마루정밀", "HH", "SEM"),
            ("CUST-HI", "여명시스템즈", "HI", "SEM"), ("CUST-HJ", "대현화학", "HJ", "SEM"),
            ("CUST-HK", "성진코리아", "HK", "SEM"), ("CUST-HL", "비전산업", "HL", "SEM"),
            ("CUST-HM", "대성화학", "HM", "SEM"), ("CUST-HN", "태평양전자", "HN", "SEM"),
            ("CUST-HO", "미래소재", "HO", "SEM"), ("CUST-HP", "동성코리아", "HP", "SEM"),
            ("CUST-HQ", "다온머티리얼즈", "HQ", "SEM"), ("CUST-HR", "대건전자", "HR", "SEM"),
            ("CUST-HS", "하늘반도체", "HS", "SEM"), ("CUST-HT", "청담소재", "HT", "SEM"),
            ("CUST-HU", "새봄정밀", "HU", "OTHER"), ("CUST-HV", "성신테크", "HV", "OTHER"),
            ("CUST-HW", "다솜시스템즈", "HW", "OTHER"), ("CUST-HX", "태평양화학", "HX", "OTHER"),
            ("CUST-HY", "여명테크", "HY", "OTHER"), ("CUST-HZ", "한빛나라산업", "HZ", "KEK"),
            ("CUST-IA", "새봄반도체", "IA", "DST"), ("CUST-IB", "우성머티리얼즈", "IB", "EUGENE"),
            ("CUST-IC", "청우산업", "IC", "TRS"), ("CUST-ID", "태평양테크", "ID", "진성"),
            ("CUST-IE", "경성산업", "IE", "YSQ"), ("CUST-IF", "은성코리아", "IF", "KKQ"),
        };
        var lineByCode = lineDefinitions.ToDictionary(l => l.Code);
        context.Customers.AddRange(extraCustomerRows.Select(r => new Customer
        {
            CustomerCode = r.Code,
            CustomerName = r.Name,
            ExportPrefix = r.ExportPrefix,
            LineDefinition = lineByCode[r.LineCode],
            CreatedAt = now,
            UpdatedAt = now
        }));

        var productAbc001 = new Product { ProductCode = "ABC-001", ItemCode = "ITEM-ABC-001", ProductName = "ABC 커넥터 001", CleaningCode = "CLN-ABC-001", Customer = customerA, CreatedAt = now, UpdatedAt = now };
        var productAbc002 = new Product { ProductCode = "ABC-002", ItemCode = "ITEM-ABC-002", ProductName = "ABC 커넥터 002", CleaningCode = "CLN-ABC-002", Customer = customerA, CreatedAt = now, UpdatedAt = now };
        var productXyz001 = new Product { ProductCode = "XYZ-001", ItemCode = "ITEM-XYZ-001", ProductName = "XYZ 모듈 001", SerialNumber = null, CleaningCode = "CLN-XYZ-001", Customer = customerB, CreatedAt = now, UpdatedAt = now };
        context.Products.AddRange(productAbc001, productAbc002, productXyz001);

        // 2026-09-01: 그리드 레이아웃(행 수 부족으로 목록이 휑하게 보이던 문제) 확인용 TEST 데이터 증량.
        // 세정코드(제품) 총 20종/제품 등록(Lot) 총 30건이 되도록 아래에서 17종/22건을 추가한다. 개발 시드
        // 전용이라 운영(공유폴더 DB)에는 영향이 없다. C사는 계속 "제품/Lot 없는 빈 업체" 케이스로 남긴다.
        var extraProductRows = new (string Code, string ItemCode, string Name, string CleaningCode, Customer Customer)[]
        {
            ("ABC-003", "ITEM-ABC-003", "ABC 커넥터 003", "CLN-ABC-003", customerA),
            ("ABC-004", "ITEM-ABC-004", "히터 블록 A", "CLN-ABC-004", customerA),
            ("ABC-005", "ITEM-ABC-005", "노즐 어셈블리 A", "CLN-ABC-005", customerA),
            ("XYZ-002", "ITEM-XYZ-002", "XYZ 모듈 002", "CLN-XYZ-002", customerB),
            ("XYZ-003", "ITEM-XYZ-003", "샤워헤드 X", "CLN-XYZ-003", customerB),
            ("XYZ-004", "ITEM-XYZ-004", "세라믹 링 X", "CLN-XYZ-004", customerB),
            ("DSN-001", "ITEM-DSN-001", "쿼츠 챔버 D", "CLN-DSN-001", customerD),
            ("DSN-002", "ITEM-DSN-002", "쿼츠 튜브 D", "CLN-DSN-002", customerD),
            ("DSN-003", "ITEM-DSN-003", "포커스 링 D", "CLN-DSN-003", customerD),
            ("ENQ-001", "ITEM-ENQ-001", "쿼츠 보트 E", "CLN-ENQ-001", customerE),
            ("ENQ-002", "ITEM-ENQ-002", "쿼츠 벨자 E", "CLN-ENQ-002", customerE),
            ("ENQ-003", "ITEM-ENQ-003", "가스 인젝터 E", "CLN-ENQ-003", customerE),
            ("FTK-001", "ITEM-FTK-001", "정전척 플레이트 F", "CLN-FTK-001", customerF),
            ("FTK-002", "ITEM-FTK-002", "히터 플레이트 F", "CLN-FTK-002", customerF),
            ("FTK-003", "ITEM-FTK-003", "엣지 링 F", "CLN-FTK-003", customerF),
            ("FTK-004", "ITEM-FTK-004", "가스 디퓨저 F", "CLN-FTK-004", customerF),
            ("FTK-005", "ITEM-FTK-005", "클램프 링 F", "CLN-FTK-005", customerF),
        };
        var extraProducts = extraProductRows.Select(r => new Product
        {
            ProductCode = r.Code,
            ItemCode = r.ItemCode,
            ProductName = r.Name,
            CleaningCode = r.CleaningCode,
            Customer = r.Customer,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        context.Products.AddRange(extraProducts);

        // C사는 아직 등록된 제품/Lot가 없는 업체 (업체 Dropdown에 존재하지만 데이터는 없는 현실적인 케이스).
        await context.SaveChangesAsync(cancellationToken);

        // 2026-08-26: 제품별 파라미터(2100 입고검사 / 7000 출고검사)를 현실적인 값으로 채운다. 이미
        // 파라미터가 있는 제품은 건드리지 않는 멱등 방식이라 기존 DB에도 안전하게 보충된다.
        await EnsureProductInspectionParametersAsync(context, cancellationToken);

        // 반출번호도 실제 채번기(EXPORT SystemSequence)를 거쳐 만든다 - 예전엔 여기서 문자열을 직접
        // 조립해 시퀀스를 올리지 않았고, 그 결과 개발 DB에서 같은 업체·같은 날짜로 신규 전산등록을 하면
        // 채번기가 이미 쓴 번호를 재발급해 반출번호 UNIQUE 충돌이 났다(2026-09-11 최종 QA에서 재현).
        // 채번기를 그대로 태우면 시퀀스가 동기화되어 이후 실제 전산등록과 충돌하지 않는다.
        var exportNumberGenerator = new ExportNumberGenerator(context);

        async Task<Lot> AddLotAsync(Product product, ProcessDefinition currentProcess, LotStatus status, DateTime receivedDate, DateTime updatedAt, int quantity)
        {
            var exportNumber = await exportNumberGenerator.NextAsync(product.CustomerId, product.Customer.ExportPrefix, receivedDate, cancellationToken);

            var registrationRecord = new Registration
            {
                Customer = product.Customer,
                Product = product,
                ExportNumber = exportNumber,
                Line = "L1",
                ProcessLabel = "STANDARD",
                ProcessRoute = standardRoute,
                RequestedQuantity = quantity,
                ShipDate = receivedDate,
                RegisteredBy = "SAMPLE-DATA",
                RegisteredAt = receivedDate
            };
            context.Registrations.Add(registrationRecord);

            var lotNumber = await lotNumberGenerator.NextAsync("SS", cancellationToken);
            var lot = new Lot
            {
                LotNumber = lotNumber,
                Product = product,
                ReceivedQuantity = quantity,
                CurrentProcessDefinition = currentProcess,
                CurrentStatus = status,
                ProcessRoute = standardRoute,
                Registration = registrationRecord,
                SerialNumber = $"{exportNumber}_1",
                ReceivedDate = receivedDate,
                CreatedAt = receivedDate,
                CreatedBy = "SAMPLE-DATA",
                UpdatedAt = updatedAt,
                UpdatedBy = "SAMPLE-DATA"
            };
            context.Lots.Add(lot);
            context.QuantityTransactions.Add(new QuantityTransaction
            {
                Lot = lot,
                TransactionType = QuantityTransactionType.Received,
                Quantity = quantity,
                OccurredAt = receivedDate,
                RecordedBy = "SAMPLE-DATA"
            });
            return lot;
        }

        void AddHistory(Lot lot, ProcessDefinition process, string worker, DateTime startedAt, DateTime? completedAt, LotStatus status, ProcessResult? result, int quantity, int defectQuantity = 0, int attempt = 1, string? remarks = null)
        {
            context.ProcessHistories.Add(new ProcessHistory
            {
                Lot = lot,
                ProcessDefinition = process,
                Worker = worker,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Status = status,
                Result = result,
                Quantity = quantity,
                DefectQuantity = defectQuantity,
                AttemptNumber = attempt,
                Remarks = remarks
            });
        }

        // 1) 정상 진행: 세정 공정 진행중
        var lot1 = await AddLotAsync(productAbc001, cleaning, LotStatus.InProgress, now.AddDays(-2), now.AddHours(-1), 500);
        AddHistory(lot1, receiving, "김생산", now.AddDays(-2), now.AddDays(-2).AddHours(1), LotStatus.Completed, null, 500);
        AddHistory(lot1, incomingInspection, "이검사", now.AddDays(-2).AddHours(2), now.AddDays(-2).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 500);
        AddHistory(lot1, cleaning, "박작업", now.AddHours(-1), null, LotStatus.InProgress, null, 500);

        // 2) HOLD: 입고검사에서 보류
        var lot2 = await AddLotAsync(productAbc001, incomingInspection, LotStatus.Hold, now.AddDays(-1), now.AddHours(-3), 300);
        AddHistory(lot2, receiving, "김생산", now.AddDays(-1), now.AddDays(-1).AddHours(1), LotStatus.Completed, null, 300);
        AddHistory(lot2, incomingInspection, "이검사", now.AddHours(-3), null, LotStatus.Hold, null, 300, remarks: "외관 Scratch 확인");
        // ProcessHistory.Status=Hold와 별개로 Hold 엔티티 행도 반드시 같이 만들어야 한다 - TRAN 엔진의
        // "작업 중지 해제"는 ProcessHistory가 아니라 이 Hold 테이블에서 미해제 행을 찾기 때문에, 이 행이
        // 없으면 실제 HOLD된 Lot인데도 해제를 시도하면 "해제할 HOLD 이력이 없습니다" 오류가 난다
        // (2026-08-18 실사용 중 재현된 버그 - Hold 상태로 시딩하는 모든 Lot마다 이 행을 빠뜨리지 않는다).
        context.Holds.Add(new Hold
        {
            Lot = lot2,
            ProcessDefinition = incomingInspection,
            RaisedBy = "이검사",
            RaisedAt = now.AddHours(-3),
            Reason = "외관 Scratch 확인",
            IsReleased = false
        });

        // 3) 재작업: 세정 2차 재작업 진행중
        var lot3 = await AddLotAsync(productAbc002, cleaning, LotStatus.Rework, now.AddDays(-3), now.AddHours(-2), 400);
        AddHistory(lot3, receiving, "김생산", now.AddDays(-3), now.AddDays(-3).AddHours(1), LotStatus.Completed, null, 400);
        AddHistory(lot3, incomingInspection, "이검사", now.AddDays(-3).AddHours(2), now.AddDays(-3).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 400);
        AddHistory(lot3, cleaning, "박작업", now.AddDays(-2), now.AddDays(-2).AddHours(1), LotStatus.Completed, ProcessResult.Fail, 380, defectQuantity: 20, attempt: 1, remarks: "1차 세정 불량");
        AddHistory(lot3, cleaning, "박작업", now.AddHours(-2), null, LotStatus.Rework, null, 380, attempt: 2, remarks: "2차 재세정 진행중");

        // 4) Rollback: 출고검사에서 세정으로 되돌아간 케이스 (RollbackHistory 자체는 Phase 9에서 추가 완료)
        var lot4 = await AddLotAsync(productAbc002, cleaning, LotStatus.InProgress, now.AddDays(-4), now.AddHours(-4), 600);
        AddHistory(lot4, receiving, "김생산", now.AddDays(-4), now.AddDays(-4).AddHours(1), LotStatus.Completed, null, 600);
        AddHistory(lot4, incomingInspection, "이검사", now.AddDays(-4).AddHours(2), now.AddDays(-4).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 600);
        AddHistory(lot4, cleaning, "박작업", now.AddDays(-3), now.AddDays(-3).AddHours(1), LotStatus.Completed, ProcessResult.Pass, 600);
        AddHistory(lot4, drying, "최작업", now.AddDays(-3).AddHours(2), now.AddDays(-3).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 600);
        AddHistory(lot4, outgoingInspection, "이검사", now.AddDays(-2), now.AddDays(-2).AddHours(1), LotStatus.Completed, ProcessResult.Fail, 600, defectQuantity: 600, remarks: "출고검사 불합격 → 세정 Rollback");
        AddHistory(lot4, cleaning, "박작업", now.AddHours(-4), null, LotStatus.InProgress, null, 600, remarks: "Rollback 재처리");

        // 5) 출하대기: 포장완료 후 고객출하 대기
        var lot5 = await AddLotAsync(productXyz001, shipping, LotStatus.Waiting, now.AddDays(-5), now.AddHours(-6), 1000);
        AddHistory(lot5, receiving, "김생산", now.AddDays(-5), now.AddDays(-5).AddHours(1), LotStatus.Completed, null, 1000);
        AddHistory(lot5, incomingInspection, "이검사", now.AddDays(-5).AddHours(2), now.AddDays(-5).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 1000);
        AddHistory(lot5, cleaning, "박작업", now.AddDays(-4), now.AddDays(-4).AddHours(1), LotStatus.Completed, ProcessResult.Pass, 1000);
        AddHistory(lot5, drying, "최작업", now.AddDays(-4).AddHours(2), now.AddDays(-4).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 1000);
        AddHistory(lot5, outgoingInspection, "이검사", now.AddDays(-3), now.AddDays(-3).AddHours(1), LotStatus.Completed, ProcessResult.Pass, 1000);
        AddHistory(lot5, packaging, "정작업", now.AddHours(-6), now.AddHours(-5), LotStatus.Completed, null, 1000);

        // 6) 완료: 고객출하까지 종료
        var lot6 = await AddLotAsync(productXyz001, shipping, LotStatus.Completed, now.AddDays(-7), now.AddDays(-1), 800);
        AddHistory(lot6, receiving, "김생산", now.AddDays(-7), now.AddDays(-7).AddHours(1), LotStatus.Completed, null, 800);
        AddHistory(lot6, incomingInspection, "이검사", now.AddDays(-7).AddHours(2), now.AddDays(-7).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 800);
        AddHistory(lot6, cleaning, "박작업", now.AddDays(-6), now.AddDays(-6).AddHours(1), LotStatus.Completed, ProcessResult.Pass, 800);
        AddHistory(lot6, drying, "최작업", now.AddDays(-6).AddHours(2), now.AddDays(-6).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 800);
        AddHistory(lot6, outgoingInspection, "이검사", now.AddDays(-5), now.AddDays(-5).AddHours(1), LotStatus.Completed, ProcessResult.Pass, 800);
        AddHistory(lot6, packaging, "정작업", now.AddDays(-4), now.AddDays(-4).AddHours(1), LotStatus.Completed, null, 800);
        AddHistory(lot6, shipping, "정작업", now.AddDays(-1), now.AddDays(-1).AddHours(1), LotStatus.Completed, null, 800);
        context.QuantityTransactions.Add(new QuantityTransaction { Lot = lot6, TransactionType = QuantityTransactionType.Shipped, Quantity = 800, OccurredAt = now.AddDays(-1).AddHours(1), RecordedBy = "정작업" });

        // 7) 장기대기: 세정 공정에서 8시간 이상 대기 중 (SystemSettings의 DelayHours=8 초과 시나리오)
        var lot7 = await AddLotAsync(productAbc001, cleaning, LotStatus.Waiting, now.AddDays(-1), now.AddHours(-9), 250);
        AddHistory(lot7, receiving, "김생산", now.AddDays(-1), now.AddDays(-1).AddHours(1), LotStatus.Completed, null, 250);
        AddHistory(lot7, incomingInspection, "이검사", now.AddDays(-1).AddHours(2), now.AddDays(-1).AddHours(3), LotStatus.Completed, ProcessResult.Pass, 250);
        AddHistory(lot7, cleaning, "박작업", now.AddHours(-9), now.AddHours(-9), LotStatus.Waiting, null, 250, remarks: "세정 대기열 적체");

        // 8) 검사 FAIL: 입고검사 불합격으로 HOLD
        var lot8 = await AddLotAsync(productAbc002, incomingInspection, LotStatus.Hold, now.AddHours(-5), now.AddHours(-4), 150);
        AddHistory(lot8, receiving, "김생산", now.AddHours(-5), now.AddHours(-4).AddMinutes(-30), LotStatus.Completed, null, 150);
        AddHistory(lot8, incomingInspection, "이검사", now.AddHours(-4), now.AddHours(-4), LotStatus.Hold, ProcessResult.Fail, 150, defectQuantity: 150, remarks: "치수 불량으로 입고검사 FAIL → HOLD");
        context.Holds.Add(new Hold
        {
            Lot = lot8,
            ProcessDefinition = incomingInspection,
            RaisedBy = "이검사",
            RaisedAt = now.AddHours(-4),
            Reason = "치수 불량으로 입고검사 FAIL → HOLD",
            IsReleased = false
        });

        // 2026-09-01: 추가 Lot 22건(총 30건). 공정/상태를 골고루 분포시켜 각 그리드(대시보드 WIP,
        // 입·출고 현황 매트릭스, HOLD 관리, 재작업 관리, TAT 조회, Batch)가 실제로 채워지도록 한다.
        var allProducts = new List<Product> { productAbc001, productAbc002, productXyz001 };
        allProducts.AddRange(extraProducts);

        // (제품 인덱스, 현재 공정, 상태, 입고 며칠 전, 수량, 비고)
        var extraLotSpecs = new (int ProductIndex, ProcessDefinition Process, LotStatus Status, int DaysAgo, int Qty, string? Remarks)[]
        {
            (3, cleaning, LotStatus.InProgress, 2, 500, null),
            (4, drying, LotStatus.InProgress, 2, 300, null),
            (5, incomingInspection, LotStatus.Waiting, 1, 250, "입고검사 대기"),
            (6, cleaning, LotStatus.Waiting, 1, 400, "세정 대기열 적체"),
            (7, laserCo2, LotStatus.InProgress, 3, 350, null),
            (8, bake, LotStatus.InProgress, 3, 600, null),
            (9, outgoingInspection, LotStatus.InProgress, 4, 200, null),
            (10, cleaning, LotStatus.Hold, 1, 150, "세정 설비 이상으로 HOLD"),
            (11, incomingInspection, LotStatus.Hold, 1, 320, "치수 확인 필요로 HOLD"),
            (12, cleaning, LotStatus.Rework, 2, 380, "재세정 진행중"),
            (13, drying, LotStatus.Rework, 2, 280, "재건조 진행중"),
            (14, packaging, LotStatus.InProgress, 5, 1000, null),
            (15, shipping, LotStatus.Completed, 7, 800, null),
            (16, shipping, LotStatus.Completed, 8, 900, null),
            (17, cleaning, LotStatus.InProgress, 1, 450, null),
            (18, drying, LotStatus.Waiting, 1, 500, "건조 대기"),
            (19, incomingInspection, LotStatus.InProgress, 2, 260, null),
            (2, bake, LotStatus.InProgress, 3, 700, null),
            (1, outgoingInspection, LotStatus.Waiting, 4, 640, "출고검사 대기"),
            (0, laserCo2, LotStatus.InProgress, 2, 480, null),
            (5, cleaning, LotStatus.InProgress, 1, 300, null),
            (8, drying, LotStatus.InProgress, 2, 520, null),
        };

        foreach (var s in extraLotSpecs)
        {
            var product = allProducts[s.ProductIndex % allProducts.Count];
            var recv = now.AddDays(-s.DaysAgo);
            var updatedAt = s.Status == LotStatus.Completed ? now.AddDays(-1) : now.AddHours(-2);
            var lot = await AddLotAsync(product, s.Process, s.Status, recv, updatedAt, s.Qty);

            // 공통: 입고 완료 이력.
            AddHistory(lot, receiving, "김생산", recv, recv.AddHours(1), LotStatus.Completed, null, s.Qty);

            if (s.Status == LotStatus.Completed && s.Process == shipping)
            {
                // 완료(고객출하)까지 전 공정 이력 + 출하 수량 트랜잭션(TAT 계산 대상).
                AddHistory(lot, incomingInspection, "이검사", recv.AddHours(2), recv.AddHours(3), LotStatus.Completed, ProcessResult.Pass, s.Qty);
                AddHistory(lot, cleaning, "박작업", recv.AddDays(1), recv.AddDays(1).AddHours(1), LotStatus.Completed, ProcessResult.Pass, s.Qty);
                AddHistory(lot, drying, "최작업", recv.AddDays(1).AddHours(2), recv.AddDays(1).AddHours(3), LotStatus.Completed, ProcessResult.Pass, s.Qty);
                AddHistory(lot, outgoingInspection, "이검사", recv.AddDays(2), recv.AddDays(2).AddHours(1), LotStatus.Completed, ProcessResult.Pass, s.Qty);
                AddHistory(lot, packaging, "정작업", recv.AddDays(2).AddHours(2), recv.AddDays(2).AddHours(3), LotStatus.Completed, null, s.Qty);
                AddHistory(lot, shipping, "정작업", updatedAt, updatedAt.AddHours(1), LotStatus.Completed, null, s.Qty);
                context.QuantityTransactions.Add(new QuantityTransaction
                {
                    Lot = lot,
                    TransactionType = QuantityTransactionType.Shipped,
                    Quantity = s.Qty,
                    OccurredAt = updatedAt.AddHours(1),
                    RecordedBy = "정작업"
                });
            }
            else
            {
                // 입고검사가 아닌 공정이면 입고검사 완료 이력을 한 줄 넣어 진행 맥락을 만든다.
                if (s.Process != incomingInspection)
                {
                    AddHistory(lot, incomingInspection, "이검사", recv.AddHours(2), recv.AddHours(3), LotStatus.Completed, ProcessResult.Pass, s.Qty);
                }

                var completedAt = s.Status == LotStatus.Completed ? updatedAt.AddHours(1) : (DateTime?)null;
                var result = s.Status == LotStatus.Hold ? ProcessResult.Fail : (ProcessResult?)null;
                AddHistory(lot, s.Process, "박작업", updatedAt, completedAt, s.Status, result, s.Qty, remarks: s.Remarks);

                if (s.Status == LotStatus.Hold)
                {
                    context.Holds.Add(new Hold
                    {
                        Lot = lot,
                        ProcessDefinition = s.Process,
                        RaisedBy = "이검사",
                        RaisedAt = updatedAt,
                        Reason = s.Remarks ?? "HOLD",
                        IsReleased = false
                    });
                }
            }
        }

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
    private static async Task EnsureTransitionDefinitionsAsync(ApplicationDbContext context, CancellationToken cancellationToken)
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

    // 2026-08-27: 기존 DB에 BROKEN/ETC(외관 Boolean) 파라미터가 없으면 제품별로 보충한다(2100/7000 양쪽).
    // 성적서 Broken/Etc 행 매칭용. 이미 검사 파라미터가 있는 제품에만 추가하고, 정렬은 기존 최대값 뒤로 붙인다.
    private static async Task EnsureExtraVisualParametersAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var extras = new (string Code, string Desc, string CertLabel)[]
        {
            ("BROKEN", "파손", "Broken"),
            ("ETC", "기타", "Etc"),
        };

        var productIds = await context.Products.Select(p => p.Id).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var productId in productIds)
        {
            var existing = await context.ParameterDefinitions
                .Where(p => p.ProductId == productId && (p.Oper == "2100" || p.Oper == "7000"))
                .ToListAsync(cancellationToken);
            if (existing.Count == 0) { continue; } // 검사 파라미터가 아직 없는 제품은 EnsureProductInspectionParameters가 처리

            var maxSort = existing.Max(p => p.SortOrder);
            foreach (var oper in new[] { "2100", "7000" })
            {
                foreach (var e in extras)
                {
                    if (existing.Any(p => p.Oper == oper && string.Equals(p.Code, e.Code, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    context.ParameterDefinitions.Add(new ParameterDefinition
                    {
                        Code = e.Code, Description = e.Desc, ParameterType = ParameterType.Boolean, Oper = oper,
                        ValueCount = 1, IsActive = true, ProductId = productId, CertificateLabel = e.CertLabel,
                        SortOrder = ++maxSort
                    });
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    // 2026-08-27: 기존 파라미터에 성적서 표기명(CertificateLabel)이 비어 있으면 코드 기준으로 보충한다.
    // 성적서 항목명과 매칭할 때 Code 대신 이 값을 우선 참조하므로, 기존 DB에도 표준 매핑을 심어준다(멱등).
    private static async Task EnsureParameterCertificateLabelsAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        // 코드(대문자) → 성적서 표기명. SCR은 과거 코드라 Scratch로 함께 매핑한다.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CHIP"] = "Chipping", ["CRACK"] = "Crack", ["BROKEN"] = "Broken",
            ["SCRATCH"] = "Scratch", ["SCR"] = "Scratch", ["PIT"] = "Pitting",
            ["ETC"] = "Etc", ["UNETCH"] = "UNETCH", ["STRIP"] = "Strip", ["STAIN"] = "Stain",
            ["WEIGHT"] = "Weight (g)", ["PARTICLE"] = "Particle",
        };

        var targets = await context.ParameterDefinitions
            .Where(p => p.CertificateLabel == null)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var p in targets)
        {
            if (map.TryGetValue(p.Code, out var label))
            {
                p.CertificateLabel = label;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    // 2026-08-26: 제품별 검사 파라미터(2100 입고검사 / 7000 출고검사)를 채운다. 이미 2100/7000 파라미터가
    // 하나라도 있는 제품은 그대로 두므로(멱등) 앱을 여러 번 켜도, 기존 DB에서도 안전하게 보충된다.
    // 외관(Boolean) 7종은 Y/N 입력, 무게/두께/표면먼지는 Numeric(MIN·MAX SPEC + 숫자 입력). 제품 Id에 따라
    // SPEC 값을 조금씩 다르게 준다(임의 값이지만 화면 형식 확인용).
    private static async Task EnsureProductInspectionParametersAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var products = await context.Products.ToListAsync(cancellationToken);
        // 외관(Boolean) 파라미터 마스터. CertLabel = 성적서 표기명(2026-08-27 피드백) - 성적서 항목명과 매칭용.
        var visuals = new (string Code, string Desc, string CertLabel)[]
        {
            ("CHIP", "깨짐", "Chipping"),
            ("CRACK", "크랙", "Crack"),
            ("BROKEN", "파손", "Broken"),
            ("SCRATCH", "긁힘", "Scratch"),
            ("PIT", "패임", "Pitting"),
            ("Etc", "기타", "Etc"),
            ("UNETCH", "막질 잔존", "UNETCH"),
            ("STRIP", "벗겨짐", "Strip"),
            ("STAIN", "얼룩", "Stain"),
        };

        var added = false;
        foreach (var product in products)
        {
            var hasInsp = await context.ParameterDefinitions.AnyAsync(
                p => p.ProductId == product.Id && (p.Oper == "2100" || p.Oper == "7000"), cancellationToken);
            if (hasInsp)
            {
                continue;
            }

            // 제품별로 SPEC를 살짝 다르게(Id 기반). 무게 45~55 기준에서 제품마다 ±.
            var baseW = 45m + (product.Id % 5) * 3m;
            var wMin = baseW; var wMax = baseW + 10m;
            var tMin = 2.90m + (product.Id % 3) * 0.05m; var tMax = tMin + 0.20m;

            var rows = new List<ParameterDefinition>();
            var sort = 0;
            void Add(string oper, string code, string desc, ParameterType type, decimal? min, decimal? max, string? certLabel = null)
                => rows.Add(new ParameterDefinition
                {
                    Code = code, Description = desc, ParameterType = type, Oper = oper,
                    ValueCount = 1, MinValue = min, MaxValue = max, Unit = null,
                    IsActive = true, ProductId = product.Id, CertificateLabel = certLabel, SortOrder = ++sort
                });

            foreach (var oper in new[] { "2100", "7000" })
            {
                foreach (var v in visuals) { Add(oper, v.Code, v.Desc, ParameterType.Boolean, null, null, v.CertLabel); }
                Add(oper, "WEIGHT", "무게(g)", ParameterType.Numeric, wMin, wMax, "Weight (g)");
                Add(oper, "THK", "두께(㎜)", ParameterType.Numeric, tMin, tMax);
            }
            Add("7000", "PARTICLE", "표면먼지(ea/sq.in)", ParameterType.Numeric, 0m, 5m, "Particle");
            Add("7000", "RESULT", "합부판정", ParameterType.Choice, null, null);

            context.ParameterDefinitions.AddRange(rows);
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
