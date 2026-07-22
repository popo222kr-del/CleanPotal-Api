using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

public class ProdReqService : IProdReqService
{
    private readonly CleanPotalDbContext _db;
    public ProdReqService(CleanPotalDbContext db) => _db = db;

    private static ProdReqDto ToDto(ProdReq p) => new(
        p.Id, p.RequestDate, p.DueDate, p.Status, p.Category, p.Location,
        p.RequestDetail, p.Requester, p.ActionDate, p.ActionDetail, p.Assignee, p.CreatedAt,
        p.RequestImages, p.ActionImages);

    // ── 등록 옵션 (구분/세부 위치/요청 분류) ──
    private static readonly ProdReqOptionsDto DefaultOptions = new(
        new[]
        {
            new ProdReqCategoryDto("METAL", new[] { "입고실", "출고실", "세정실", "반입구" }),
            new ProdReqCategoryDto("N-METAL", new[] { "입고실", "출고실", "세정실", "반입구" }),
            new ProdReqCategoryDto("레이저실", new[] { "LASER", "CO2", "각인기", "기타" }),
            new ProdReqCategoryDto("기타", new[] { "기타" }),
        },
        new[] { "소모품", "수리", "내용", "기타" });

    public async Task<ProdReqOptionsDto> GetOptionsAsync()
    {
        var rows = await _db.ProdReqOptions.OrderBy(o => o.OrderIndex).ToListAsync();
        if (rows.Count == 0) return DefaultOptions;   // 마이그레이션 전/비어있을 때 기본값
        var cats = rows.Where(r => r.Kind == "category")
            .Select(c => new ProdReqCategoryDto(
                c.Name,
                rows.Where(r => r.Kind == "subloc" && r.Parent == c.Name).Select(r => r.Name).ToList()))
            .ToList();
        var types = rows.Where(r => r.Kind == "reqtype").Select(r => r.Name).ToList();
        if (cats.Count == 0) cats = DefaultOptions.Categories.ToList();
        if (types.Count == 0) types = DefaultOptions.ReqTypes.ToList();
        return new ProdReqOptionsDto(cats, types);
    }

    public async Task<ProdReqOptionsDto> SaveOptionsAsync(ProdReqOptionsDto dto)
    {
        // 전체 교체 저장 (기존 요청 데이터는 문자열로 저장되어 있어 영향 없음)
        var cats = dto.Categories
            .Select(c => new ProdReqCategoryDto(
                (c.Name ?? "").Trim(),
                (c.Subs ?? Array.Empty<string>()).Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Distinct().ToList()))
            .Where(c => c.Name.Length > 0)
            .GroupBy(c => c.Name).Select(g => g.First())
            .ToList();
        var types = (dto.ReqTypes ?? Array.Empty<string>())
            .Select(t => (t ?? "").Trim()).Where(t => t.Length > 0).Distinct().ToList();
        if (cats.Count == 0 || types.Count == 0)
            throw new InvalidOperationException("구분과 요청 분류는 최소 1개 이상 있어야 합니다.");

        _db.ProdReqOptions.RemoveRange(_db.ProdReqOptions);
        int ord = 0;
        foreach (var c in cats)
        {
            _db.ProdReqOptions.Add(new ProdReqOption { Kind = "category", Name = c.Name, OrderIndex = ord++ });
            foreach (var sVal in c.Subs)
                _db.ProdReqOptions.Add(new ProdReqOption { Kind = "subloc", Name = sVal, Parent = c.Name, OrderIndex = ord++ });
        }
        foreach (var t in types)
            _db.ProdReqOptions.Add(new ProdReqOption { Kind = "reqtype", Name = t, OrderIndex = ord++ });
        await _db.SaveChangesAsync();
        return new ProdReqOptionsDto(cats, types);
    }

    public async Task<IReadOnlyList<ProdReqDto>> GetAllAsync(string? status, string? search)
    {
        var q = _db.ProdReqs.AsQueryable();
        if (!string.IsNullOrEmpty(status) && status != "전체") q = q.Where(p => p.Status == status);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(p => p.RequestDetail.Contains(search) || p.Location.Contains(search) ||
                             p.Requester.Contains(search) || p.Category.Contains(search));
        // WPF: 요청일 내림차순, 예정일 오름차순
        var items = await q.OrderByDescending(p => p.RequestDate).ThenBy(p => p.DueDate).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<ProdReqDto> CreateAsync(ProdReqUpsertRequest req, string requester)
    {
        var p = new ProdReq
        {
            RequestDate = req.RequestDate ?? DateOnly.FromDateTime(DateTime.Today),
            DueDate = req.DueDate,
            Status = "진행",
            Category = req.Category,
            Location = req.Location,
            RequestDetail = req.RequestDetail,
            Requester = requester,
            ActionDate = req.ActionDate,
            ActionDetail = req.ActionDetail,
            Assignee = req.Assignee,
            RequestImages = req.RequestImages ?? "",
            ActionImages = req.ActionImages ?? "",
            CreatedAt = DateTime.Now,
        };
        _db.ProdReqs.Add(p);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    /// <summary>조치/수정 저장. 담당자는 조치 내용(텍스트·사진·상태)이 실제로 바뀐 경우에만
    /// 현재 사용자로 자동 기록 — 요청 문구만 고친 요청자가 담당자로 기록되는 것 방지.
    /// 상태가 오면 함께 반영(완료→완료일 오늘, 그 외→완료일 해제).</summary>
    public async Task<ProdReqDto?> UpdateAsync(int id, ProdReqUpsertRequest req, string actor)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return null;
        bool actionChanged =
            p.ActionDetail != req.ActionDetail ||
            (req.ActionImages is not null && req.ActionImages != p.ActionImages) ||
            (!string.IsNullOrEmpty(req.Status) && req.Status != p.Status);
        bool isRequester = p.Requester == actor;
        p.RequestDate = req.RequestDate;
        p.DueDate = req.DueDate;
        p.Category = req.Category;
        p.Location = req.Location;
        // 원본 요청 내용/이미지는 등록자만 수정 가능 — 타인이 보내면 무시하고 원본 보존
        if (isRequester) p.RequestDetail = req.RequestDetail;
        p.ActionDetail = req.ActionDetail;
        if (actionChanged) p.Assignee = actor;    // 조치 변경 시에만 담당자 갱신
        if (isRequester && req.RequestImages is not null) p.RequestImages = req.RequestImages;
        if (req.ActionImages is not null) p.ActionImages = req.ActionImages;
        if (!string.IsNullOrEmpty(req.Status))
        {
            bool wasDone = p.Status == "완료";
            p.Status = req.Status;
            if (req.Status == "완료")
            {
                // 완료로 '전환'될 때만 오늘 도장 — 이미 완료된 항목 재저장 시 원래 완료일 보존
                if (!wasDone || p.ActionDate is null) p.ActionDate = DateOnly.FromDateTime(DateTime.Today);
            }
            else
            {
                p.ActionDate = null;
            }
        }
        else
        {
            p.ActionDate = req.ActionDate;
        }
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<ProdReqDto?> ChangeStatusAsync(int id, string status)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return null;
        p.Status = status;
        if (status == "완료") p.ActionDate ??= DateOnly.FromDateTime(DateTime.Today);
        await _db.SaveChangesAsync();
        return ToDto(p);
    }

    public async Task<bool> DeleteAsync(int id, string actor, bool isAdmin)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return false;
        if (p.Requester != actor && !isAdmin)
            throw new InvalidOperationException("요청 등록자만 삭제할 수 있습니다.");
        _db.ProdReqs.Remove(p);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── 미확인 뱃지 (WPF ProdReqReadState) ──

    public async Task<int> GetUnreadCountAsync(string username)
    {
        if (string.IsNullOrEmpty(username)) return 0;
        var rs = await _db.ProdReqReads.FindAsync(username);
        if (rs is null)
        {
            // 최초 사용자는 지금 기준으로 초기화 → 기존 요청이 전부 미확인으로 뜨지 않게 (WPF 동일)
            try
            {
                _db.ProdReqReads.Add(new ProdReqRead { Username = username, LastReadTime = DateTime.Now });
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // 다른 탭/요청이 먼저 삽입한 경우 (PK 충돌) — 그냥 0 반환
                _db.ChangeTracker.Clear();
            }
            return 0;
        }
        return await _db.ProdReqs.CountAsync(p => p.CreatedAt > rs.LastReadTime);
    }

    public async Task MarkReadAsync(string username)
    {
        if (string.IsNullOrEmpty(username)) return;
        var rs = await _db.ProdReqReads.FindAsync(username);
        try
        {
            if (rs is null) _db.ProdReqReads.Add(new ProdReqRead { Username = username, LastReadTime = DateTime.Now });
            else rs.LastReadTime = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // 동시 삽입 충돌 — 이미 존재하는 행을 갱신
            _db.ChangeTracker.Clear();
            var again = await _db.ProdReqReads.FindAsync(username);
            if (again is not null) { again.LastReadTime = DateTime.Now; await _db.SaveChangesAsync(); }
        }
    }
}
