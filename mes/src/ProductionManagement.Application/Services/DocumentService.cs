using ProductionManagement.Application.DTOs;
using ProductionManagement.Application.Exceptions;
using ProductionManagement.Application.Interfaces;
using ProductionManagement.Domain.Entities;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Application.Services;

// 성적서 등 LOT 문서 관리 구현. 파일 실체는 공유폴더(FileStorage)에 복사해 두고 DB에는 경로만 남긴다.
// 같은 LOT에 같은 이름의 문서를 또 올리면 버전을 올려 이전 것을 덮지 않고 함께 보관한다.
public class DocumentService : IDocumentService
{
    private readonly IRepository<Document, int> _documents;
    private readonly IRepository<Lot, int> _lots;
    private readonly IFileStorageService _fileStorageService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserProvider _currentUser;
    private readonly IAuthorizationService _authorization;

    public DocumentService(
        IRepository<Document, int> documents,
        IRepository<Lot, int> lots,
        IFileStorageService fileStorageService,
        IUnitOfWork unitOfWork,
        IAuditLogger auditLogger,
        ICurrentUserProvider currentUser,
        IAuthorizationService authorization)
    {
        _documents = documents;
        _lots = lots;
        _fileStorageService = fileStorageService;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    public async Task<IReadOnlyList<DocumentDto>> GetByLotIdAsync(int lotId, CancellationToken cancellationToken = default)
    {
        var documents = await _documents.ListAsync(d => d.LotId == lotId, cancellationToken);
        var lot = await _lots.GetByIdAsync(lotId, cancellationToken);

        return documents
            .OrderByDescending(d => d.DocumentVersion)
            .Select(d => ToDto(d, lot?.LotNumber ?? "-"))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentDto>> SearchAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        var documents = await _documents.ListAllAsync(cancellationToken);
        var lots = (await _lots.ListAllAsync(cancellationToken)).ToDictionary(l => l.Id);

        var query = documents.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(d =>
                d.FileName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (lots.TryGetValue(d.LotId, out var lot) && lot.LotNumber.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => ToDto(d, lots.TryGetValue(d.LotId, out var lot) ? lot.LotNumber : "-"))
            .ToList();
    }

    public async Task<DocumentDto> UploadAsync(DocumentUploadRequest request, CancellationToken cancellationToken = default)
    {
        await _authorization.EnsurePermissionAsync(PermissionCode.AdminCertificate, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.SourceFilePath))
        {
            throw new ValidationException(new[] { "업로드할 파일을 선택하세요." });
        }

        var lot = await _lots.GetByIdAsync(request.LotId, cancellationToken)
            ?? throw new InvalidOperationException("Lot를 찾을 수 없습니다.");

        var actor = _currentUser.GetCurrentUser();
        var now = DateTime.Now;

        var relativePath = await _fileStorageService.SaveAsync(lot.LotNumber, request.SourceFilePath, now, cancellationToken);

        var existing = await _documents.ListAsync(d => d.LotId == request.LotId, cancellationToken);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(d => d.DocumentVersion) + 1;

        var document = new Document
        {
            Lot = lot,
            FileName = Path.GetFileName(request.SourceFilePath),
            FilePath = relativePath,
            DocumentVersion = nextVersion,
            CreatedBy = actor,
            CreatedAt = now
        };

        await _documents.AddAsync(document, cancellationToken);
        _auditLogger.Log(nextVersion == 1 ? "Document.Register" : "Document.Update", nameof(Document), lot.LotNumber, actor,
            $"FileName={document.FileName}, Version={nextVersion}");

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(document, lot.LotNumber);
    }

    public string GetFullPath(DocumentDto document) => _fileStorageService.GetFullPath(document.FilePath);

    private static DocumentDto ToDto(Document document, string lotNumber) => new(
        document.Id,
        document.LotId,
        lotNumber,
        document.FileName,
        document.FilePath,
        document.DocumentVersion,
        document.CreatedBy,
        document.CreatedAt);
}
