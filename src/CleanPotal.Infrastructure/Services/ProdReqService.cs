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

    /// <summary>조치/수정 저장. WPF와 동일: 담당자는 항상 현재 사용자로 자동 기록,
    /// 상태가 오면 함께 반영(완료→완료일 오늘, 그 외→완료일 해제).</summary>
    public async Task<ProdReqDto?> UpdateAsync(int id, ProdReqUpsertRequest req, string actor)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return null;
        p.RequestDate = req.RequestDate;
        p.DueDate = req.DueDate;
        p.Category = req.Category;
        p.Location = req.Location;
        p.RequestDetail = req.RequestDetail;
        p.ActionDetail = req.ActionDetail;
        p.Assignee = actor;                       // 자동 기록 (감사 추적)
        if (req.RequestImages is not null) p.RequestImages = req.RequestImages;
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

    public async Task<bool> DeleteAsync(int id)
    {
        var p = await _db.ProdReqs.FindAsync(id);
        if (p is null) return false;
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
