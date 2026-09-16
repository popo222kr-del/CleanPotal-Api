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
            await MesBaseDataSeeder.EnsureTransitionDefinitionsAsync(context, cancellationToken);
            // 기존 DB에 BROKEN/ETC(외관 Boolean)가 없으면 제품별로 보충한다(2026-08-27 피드백).
            await EnsureExtraVisualParametersAsync(context, cancellationToken);
            // 기존 파라미터에 성적서 표기명(CertificateLabel)이 비어 있으면 코드로 보충한다(2026-08-27 피드백).
            await EnsureParameterCertificateLabelsAsync(context, cancellationToken);
            return;
        }

        // 기준 데이터(공정·플로우·TRAN·사유코드·LINE·레시피)는 환경을 가리지 않고 깔린다.
        // 여기서는 그 위에 개발용 샘플(가짜 업체·제품·LOT)만 얹는다.
        await MesBaseDataSeeder.SeedAsync(context, cancellationToken);

        var now = DateTime.Now;
        var processes = await context.ProcessDefinitions.ToListAsync(cancellationToken);
        ProcessDefinition Proc(int operCode) => processes.First(p => p.OperCode == operCode);
        var receiving = Proc(2000);
        var incomingInspection = Proc(2100);
        var cleaning = Proc(3000);
        var drying = Proc(4000);
        var laserCo2 = Proc(4100);
        var bake = Proc(5000);
        var outgoingInspection = Proc(7000);
        var packaging = Proc(7100);
        var shipping = Proc(8100);
        var standardRoute = await context.ProcessRoutes.FirstAsync(r => r.RouteCode == "STANDARD", cancellationToken);
        var lineDefinitions = await context.LineDefinitions.ToListAsync(cancellationToken);


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
