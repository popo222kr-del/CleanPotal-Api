using Microsoft.Extensions.Configuration;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;

namespace ProductionManagement.Infrastructure.Services;

// Phase 3 구현: 전산등록 시 LOT별 성적서(양식 복사본, 제목=LOT번호)를 생성해 문서로 등록한다.
//  - 양식 원본: 우선 Product.CertificateTemplateData(제품 셋업에서 '불러오기'로 저장한 바이트)를 쓰고,
//    없으면 INSPECTION/templates/{CertificateTemplateFileName} 파일을 읽는다(수기 파일명 방식).
//  - 저장 위치: IFileStorageService(=INSPECTION 폴더) 아래 LOT별 경로. 파일명은 {LOT번호}{확장자}.
//  - 값 채우기(검사 파라미터 반영)는 Phase 4에서. 지금은 양식을 그대로 복사만 한다.
public class LotCertificateService : ILotCertificateService
{
    private readonly IRepository<Lot, int> _lots;
    private readonly IRepository<Product, int> _products;
    private readonly IRepository<Document, int> _documents;
    private readonly IFileStorageService _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserProvider _currentUser;
    private readonly ICertificateFillService _fillService;
    private readonly string _rootPath;

    public LotCertificateService(
        IRepository<Lot, int> lots,
        IRepository<Product, int> products,
        IRepository<Document, int> documents,
        IFileStorageService fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentUserProvider currentUser,
        ICertificateFillService fillService,
        IConfiguration configuration)
    {
        _lots = lots;
        _products = products;
        _documents = documents;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _fillService = fillService;
        _rootPath = configuration["Documents:RootPath"] ?? "INSPECTION";
    }

    public async Task<bool> TryGenerateForLotAsync(int lotId, CancellationToken cancellationToken = default)
    {
        try
        {
            var lot = await _lots.GetByIdAsync(lotId, cancellationToken);
            if (lot is null) { return false; }
            var product = await _products.GetByIdAsync(lot.ProductId, cancellationToken);
            if (product is null) { return false; }

            var templateBytes = await ResolveTemplateBytesAsync(product);
            if (templateBytes is null || templateBytes.Length == 0) { return false; }

            var ext = string.IsNullOrWhiteSpace(product.CertificateTemplateFileName)
                ? ".xlsx"
                : (Path.GetExtension(product.CertificateTemplateFileName) is { Length: > 0 } e ? e : ".xlsx");

            // 임시 파일을 LOT번호로 만들어 저장소가 그 이름 그대로 복사하게 한다(제목=LOT번호).
            var tempPath = Path.Combine(Path.GetTempPath(), $"{Sanitize(lot.LotNumber)}{ext}");
            await File.WriteAllBytesAsync(tempPath, templateBytes, cancellationToken);
            try
            {
                var now = DateTime.Now;
                var relativePath = await _fileStorage.SaveAsync(lot.LotNumber, tempPath, now, cancellationToken);

                var existing = await _documents.ListAsync(d => d.LotId == lotId, cancellationToken);
                var nextVersion = existing.Count == 0 ? 1 : existing.Max(d => d.DocumentVersion) + 1;

                await _documents.AddAsync(new Document
                {
                    LotId = lotId,
                    FileName = $"{lot.LotNumber}{ext}",
                    FilePath = relativePath,
                    DocumentVersion = nextVersion,
                    CreatedBy = _currentUser.GetCurrentUser(),
                    CreatedAt = now
                }, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 2026-08-31 피드백(#10): 등록 시점엔 성적서 파일만 만들어 두고 값(Part Info/검사값)은 채우지
                // 않는다. 실제 값 반영은 입고검사(2100)·출고검사(7000) 완료 시에만 한다(OperActionService).
                return true;
            }
            finally
            {
                try { if (File.Exists(tempPath)) { File.Delete(tempPath); } } catch { /* 임시파일 정리 실패는 무시 */ }
            }
        }
        catch
        {
            // 성적서 생성 실패가 전산등록 자체를 막으면 안 된다.
            return false;
        }
    }

    private async Task<byte[]?> ResolveTemplateBytesAsync(Product product)
    {
        // 1) 제품에 저장된 양식 바이트(제품 셋업에서 '불러오기'로 저장)를 우선 쓴다.
        if (product.CertificateTemplateData is { Length: > 0 } bytes)
        {
            return bytes;
        }

        // 2) 세정코드별 기본 양식을 공유폴더(기타 Inspection)에서 찾는다(2026-08-28 피드백).
        //    후보: templates 하위 + 루트 직접, 저장된 파일명 or 세정코드 기반(xlsx/xlsm/xls).
        var candidates = new List<string>();
        void AddCandidates(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) { return; }
            candidates.Add(Path.Combine(_rootPath, "templates", name));
            candidates.Add(Path.Combine(_rootPath, name));
        }

        AddCandidates(product.CertificateTemplateFileName);
        if (!string.IsNullOrWhiteSpace(product.CleaningCode))
        {
            foreach (var ext in new[] { ".xlsx", ".xlsm", ".xls" })
            {
                AddCandidates(product.CleaningCode + ext);
            }
        }

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return await File.ReadAllBytesAsync(path);
            }
        }
        return null;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
