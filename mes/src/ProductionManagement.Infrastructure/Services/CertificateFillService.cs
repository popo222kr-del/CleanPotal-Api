using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Services;

// Phase 4 구현: LOT별 성적서 파일에 앱 데이터를 반영한다.
//  - Part Information(고객/제품/세정코드/수량/LINE/품목코드/PROCESS/S-N/일자)는 항상 채운다.
//  - 입·출고 검사값(외관 Y/N, 무게/표면먼지)은 그 시점까지 기록된 InspectionRecord를 모아 함께 채운다
//    (입고검사 저장 후엔 입고 값이, 출고검사 저장 후엔 출고 값까지 반영 - 공정 이동 시 자동 반영).
//  - 실제 셀 쓰기는 ICertificateExcelFiller(Excel COM, Certificate 프로젝트)에 위임한다. Infrastructure는
//    COM에 의존하지 않는다. 반영 실패가 등록/검사 저장을 막으면 안 되므로 예외는 삼키고 false만 돌린다.
public class CertificateFillService : ICertificateFillService
{
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<Customer, int> _customers;
    private readonly IRepository<LineDefinition, int> _lines;
    private readonly IRepository<Registration, int> _registrations;
    private readonly IRepository<Document, int> _documents;
    private readonly IRepository<InspectionRecord, int> _inspectionRecords;
    private readonly IRepository<ParameterDefinition, int> _parameterDefinitions;
    private readonly IRepository<ProcessHistory, int> _processHistories;
    private readonly IRepository<ProcessDefinition, int> _processDefinitions;
    private readonly IRepository<User, int> _users;
    private readonly IFileStorageService _fileStorage;
    private readonly ICertificateExcelFiller _filler;

    // 출고검사 OPER 코드(담당 인원 = 이 공정의 작업자).
    private const int OutgoingInspectionOperCode = 7000;

    public CertificateFillService(
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<Customer, int> customers,
        IRepository<LineDefinition, int> lines,
        IRepository<Registration, int> registrations,
        IRepository<Document, int> documents,
        IRepository<InspectionRecord, int> inspectionRecords,
        IRepository<ParameterDefinition, int> parameterDefinitions,
        IRepository<ProcessHistory, int> processHistories,
        IRepository<ProcessDefinition, int> processDefinitions,
        IRepository<User, int> users,
        IFileStorageService fileStorage,
        ICertificateExcelFiller filler)
    {
        _lots = lots;
        _products = products;
        _customers = customers;
        _lines = lines;
        _registrations = registrations;
        _documents = documents;
        _inspectionRecords = inspectionRecords;
        _parameterDefinitions = parameterDefinitions;
        _processHistories = processHistories;
        _processDefinitions = processDefinitions;
        _users = users;
        _fileStorage = fileStorage;
        _filler = filler;
    }

    // 7000(출고검사) 작업자 이름을 구한다. 그 LOT의 7000 공정 이력 중 무효화되지 않은 가장 최근 Worker를
    // 찾고, 그 Worker(로그인 아이디)를 사용자 표시명(DisplayName)으로 바꿀 수 있으면 바꾼다.
    private async Task<string> ResolveOutgoingInspectorAsync(int lotId, CancellationToken cancellationToken)
    {
        var outgoing = (await _processDefinitions.ListAsync(p => p.OperCode == OutgoingInspectionOperCode, cancellationToken))
            .FirstOrDefault();
        if (outgoing is null) { return string.Empty; }

        var worker = (await _processHistories.ListAsync(
                h => h.LotId == lotId && h.ProcessDefinitionId == outgoing.Id && !h.IsVoided,
                cancellationToken))
            .OrderByDescending(h => h.CompletedAt ?? h.StartedAt).ThenByDescending(h => h.Id)
            .Select(h => h.Worker)
            .FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));
        if (string.IsNullOrWhiteSpace(worker)) { return string.Empty; }

        var user = (await _users.ListAsync(u => u.LoginId == worker, cancellationToken)).FirstOrDefault();
        return string.IsNullOrWhiteSpace(user?.DisplayName) ? worker! : user!.DisplayName!;
    }

    public async Task<bool> FillAsync(int lotId, CancellationToken cancellationToken = default)
    {
        try
        {
            var lot = await _lots.GetByIdAsync(lotId, cancellationToken);
            if (lot is null) { return false; }
            var product = await _products.GetByIdAsync(lot.ProductId, cancellationToken);
            if (product is null) { return false; }
            var customer = await _customers.GetByIdAsync(product.CustomerId, cancellationToken);

            // LINE은 자유 텍스트가 아니라 마스터(고객사 LINE 정의)의 LINE 코드를 쓴다(2026-08-27 피드백).
            LineDefinition? line = customer?.LineDefinitionId is int lineId
                ? await _lines.GetByIdAsync(lineId, cancellationToken)
                : null;

            Registration? registration = lot.RegistrationId is int regId
                ? await _registrations.GetByIdAsync(regId, cancellationToken)
                : null;

            // 이 LOT의 최신 성적서 파일(버전 최대)을 찾아 절대경로로 푼다.
            var documents = await _documents.ListAsync(d => d.LotId == lotId, cancellationToken);
            if (documents.Count == 0) { return false; }
            var latest = documents.OrderByDescending(d => d.DocumentVersion).First();
            var fullPath = _fileStorage.GetFullPath(latest.FilePath);
            if (!File.Exists(fullPath)) { return false; }

            // 이 LOT의 검사값을 (Code, Oper) 단위로 모은다.
            var records = await _inspectionRecords.ListAsync(r => r.LotId == lotId, cancellationToken);
            var values = new List<CertificateInspectionValue>();
            if (records.Count > 0)
            {
                var defs = (await _parameterDefinitions.ListAsync(p => p.ProductId == product.Id, cancellationToken))
                    .ToDictionary(p => p.Id);
                foreach (var record in records)
                {
                    if (!defs.TryGetValue(record.ParameterDefinitionId, out var def)) { continue; }
                    if (def.Oper is not ("2100" or "7000")) { continue; }
                    values.Add(new CertificateInspectionValue(
                        def.Code, def.Oper!, record.InputValue, def.MinValue, def.MaxValue, def.ParameterType,
                        def.CertificateLabel));
                }
            }

            // 2026-08-31 피드백(#7): 담당 인원 = 7000(출고검사) 작업자 이름. 그 LOT의 7000 공정 이력 중
            // 가장 최근(무효화 제외) Worker를 쓰고, 사용자 표시명(DisplayName)이 있으면 그것으로 바꾼다.
            var outgoingInspector = await ResolveOutgoingInspectorAsync(lotId, cancellationToken);

            // LINE USER 코드는 앞의 "I" 표시를 제외한다(2026-08-31 피드백 #7).
            var lineUserCode = line?.UserCode ?? string.Empty;
            if (lineUserCode.StartsWith("I", StringComparison.OrdinalIgnoreCase))
            {
                lineUserCode = lineUserCode[1..];
            }

            var data = new CertificateFillData(
                LineName: line?.Description ?? string.Empty,
                LineUserCode: lineUserCode,
                UnitMaker: registration?.PmEquipmentName ?? string.Empty,
                TeamCode: registration?.TeamName ?? string.Empty,
                OutgoingInspector: outgoingInspector,
                PartName: product.ProductName,
                CleaningCode: product.CleaningCode,
                Quantity: lot.ReceivedQuantity,
                ItemCode: product.ItemCode,
                Process: registration?.ProcessLabel ?? string.Empty,
                SerialNumber: lot.SerialNumber,
                InspectionDate: DateTime.Now,
                Values: values);

            // Excel COM은 동기 작업이라 UI 스레드를 막지 않도록 백그라운드로 돌린다.
            await Task.Run(() => _filler.Fill(fullPath, data), cancellationToken);
            return true;
        }
        catch
        {
            // 성적서 반영 실패가 등록/검사 저장 자체를 막으면 안 된다.
            return false;
        }
    }

    public async Task<bool> InsertAbnormalImageAsync(int lotId, string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) { return false; }

            var certPath = await ResolveLatestCertificatePathAsync(lotId, cancellationToken);
            if (certPath is null) { return false; }

            await Task.Run(() => _filler.InsertImage(certPath, imagePath), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 이 LOT의 최신 성적서 파일 절대경로를 찾는다(없으면 null).
    private async Task<string?> ResolveLatestCertificatePathAsync(int lotId, CancellationToken cancellationToken)
    {
        var documents = await _documents.ListAsync(d => d.LotId == lotId, cancellationToken);
        if (documents.Count == 0) { return null; }
        var latest = documents.OrderByDescending(d => d.DocumentVersion).First();
        var fullPath = _fileStorage.GetFullPath(latest.FilePath);
        return File.Exists(fullPath) ? fullPath : null;
    }
}
