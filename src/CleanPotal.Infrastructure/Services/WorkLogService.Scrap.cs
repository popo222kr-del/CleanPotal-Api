using System.Text.RegularExpressions;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>
/// 폐기품 관리 — 엑셀 "폐기품 LIST(.xlsm)".
/// 엑셀에서는 '폐기품_생산' 시트에 적고 → 매크로로 라인 순 '폐기품_물류' 를 만들어 출력 → 상차 후 '폐기품_이력' 에 붙여 쌓았다.
/// 여기서는 LIST 하나(ScrapBatch)에 줄을 적고, 매칭 확인·상차를 폰에서 바로 체크하고, 상차가 끝나면 '상차 완료' 로 닫는다.
/// 눈관리 요청(ScrapTag)과 분임조 → 라인·담당자 표(ScrapCircle)도 같이 둔다.
/// </summary>
public partial class WorkLogService
{
    private static string Cut(string? s, int n)
    {
        var t = Regex.Replace((s ?? "").Trim(), @"\s*\r?\n\s*", " ");
        return t.Length > n ? t[..n] : t;
    }

    private static ScrapItemDto ToDto(ScrapItem i) => new(i.Id, i.Line, i.MatId, i.MatDesc, i.SerialNo, i.OutNo,
        i.Matched, i.Loaded, i.Remark, i.SortOrder, i.UpdatedBy);
    private static ScrapTagDto ToDto(ScrapTag t) => new(t.Id, t.Date, t.Writer, t.Item, t.SerialNo, t.Line, t.Circle,
        t.Owner, t.Note, t.Done, t.UpdatedBy);

    public async Task<IReadOnlyList<ScrapBatchSummaryDto>> GetScrapBatchesAsync()
        => await _db.ScrapBatches.AsNoTracking()
            .OrderBy(b => b.IsClosed).ThenByDescending(b => b.Date).ThenByDescending(b => b.Id)
            .Select(b => new ScrapBatchSummaryDto(b.Id, b.Date, b.Title, b.IsClosed,
                b.Items.Count, b.Items.Count(i => i.Matched), b.Items.Count(i => i.Loaded)))
            .ToListAsync();

    public async Task<ScrapBatchDto> GetScrapBatchAsync(int id)
    {
        var b = await _db.ScrapBatches.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new BusinessRuleException("폐기품 LIST 를 찾을 수 없습니다.");
        return new ScrapBatchDto(b.Id, b.Date, b.Title, b.Note, b.IsClosed, b.ClosedAt, b.ClosedBy, b.CreatedBy,
            b.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ToDto).ToList());
    }

    /// <summary>LIST 머리 저장. id 가 0 이면 새로 만든다. 상차 완료로 바꾸면 닫은 사람·시각을 남긴다.</summary>
    public async Task<ScrapBatchDto> SaveScrapBatchAsync(int id, ScrapBatchSaveRequest r, string actor)
    {
        ScrapBatch b;
        if (id == 0)
        {
            b = new ScrapBatch { CreatedBy = actor, CreatedAt = DateTime.Now };
            _db.ScrapBatches.Add(b);
        }
        else
        {
            b = await _db.ScrapBatches.FirstOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("폐기품 LIST 를 찾을 수 없습니다.");
        }
        b.Date = r.Date;
        b.Title = Cut(r.Title, 100);
        b.Note = Cut(r.Note, 500);
        if (r.IsClosed && !b.IsClosed) { b.ClosedAt = DateTime.Now; b.ClosedBy = actor; }
        if (!r.IsClosed) { b.ClosedAt = null; b.ClosedBy = ""; }
        b.IsClosed = r.IsClosed;
        await _db.SaveChangesAsync();
        return await GetScrapBatchAsync(b.Id);
    }

    public async Task DeleteScrapBatchAsync(int id)
    {
        var n = await _db.ScrapBatches.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (n == 0) throw new BusinessRuleException("폐기품 LIST 를 찾을 수 없습니다.");
    }

    private static bool IsEmpty(ScrapItemSaveRequest r)
        => string.IsNullOrWhiteSpace(r.Line) && string.IsNullOrWhiteSpace(r.MatId) && string.IsNullOrWhiteSpace(r.MatDesc)
           && string.IsNullOrWhiteSpace(r.SerialNo) && string.IsNullOrWhiteSpace(r.OutNo) && string.IsNullOrWhiteSpace(r.Remark);

    private static void Apply(ScrapItem i, ScrapItemSaveRequest r, string actor)
    {
        i.Line = Cut(r.Line, 30); i.MatId = Cut(r.MatId, 30); i.MatDesc = Cut(r.MatDesc, 100);
        i.SerialNo = Cut(r.SerialNo, 60); i.OutNo = Cut(r.OutNo, 40); i.Remark = Cut(r.Remark, 200);
        i.Matched = r.Matched; i.Loaded = r.Loaded;
        i.UpdatedBy = actor; i.UpdatedAt = DateTime.Now;
    }

    /// <summary>줄 여러 개 넣기(붙여넣기·한 줄 추가). 빈 줄은 건너뛴다. 닫힌 LIST 에는 넣지 않는다.</summary>
    public async Task<ScrapBatchDto> AddScrapItemsAsync(int batchId, IReadOnlyList<ScrapItemSaveRequest> items, string actor)
    {
        var b = await _db.ScrapBatches.FirstOrDefaultAsync(x => x.Id == batchId) ?? throw new BusinessRuleException("폐기품 LIST 를 찾을 수 없습니다.");
        if (b.IsClosed) throw new BusinessRuleException("상차 완료한 LIST 입니다. 다시 열고 넣으세요.");
        if (items is null || items.Count == 0) throw new BusinessRuleException("넣을 줄이 없습니다.");
        if (items.Count > 2000) throw new BusinessRuleException("한 번에 2000줄까지 넣을 수 있습니다.");
        var order = await _db.ScrapItems.Where(i => i.BatchId == batchId).Select(i => (int?)i.SortOrder).MaxAsync() ?? 0;
        foreach (var r in items.Where(r => !IsEmpty(r)))
        {
            var it = new ScrapItem { BatchId = batchId, SortOrder = ++order };
            Apply(it, r, actor);
            _db.ScrapItems.Add(it);
        }
        await _db.SaveChangesAsync();
        return await GetScrapBatchAsync(batchId);
    }

    /// <summary>
    /// 줄 저장. 닫힌 LIST 도 매칭·상차 체크와 특이사항은 고칠 수 있다(늦게 체크하는 경우) — 품목 칸은 LIST 를 다시 열어야 고친다.
    /// </summary>
    public async Task<ScrapItemDto> SaveScrapItemAsync(int id, ScrapItemSaveRequest r, string actor)
    {
        var it = await _db.ScrapItems.Include(i => i.Batch).FirstOrDefaultAsync(i => i.Id == id)
                 ?? throw new BusinessRuleException("줄을 찾을 수 없습니다.");
        if (it.Batch!.IsClosed && (Cut(r.Line, 30) != it.Line || Cut(r.MatId, 30) != it.MatId || Cut(r.MatDesc, 100) != it.MatDesc
                                   || Cut(r.SerialNo, 60) != it.SerialNo || Cut(r.OutNo, 40) != it.OutNo))
            throw new BusinessRuleException("상차 완료한 LIST 의 품목은 LIST 를 다시 열고 고치세요.");
        if (IsEmpty(r)) throw new BusinessRuleException("빈 줄로 저장할 수 없습니다. 지우려면 '지우기' 를 누르세요.");
        Apply(it, r, actor);
        await _db.SaveChangesAsync();
        return ToDto(it);
    }

    public async Task DeleteScrapItemAsync(int id)
    {
        var it = await _db.ScrapItems.Include(i => i.Batch).FirstOrDefaultAsync(i => i.Id == id)
                 ?? throw new BusinessRuleException("줄을 찾을 수 없습니다.");
        if (it.Batch!.IsClosed) throw new BusinessRuleException("상차 완료한 LIST 입니다. 다시 열고 지우세요.");
        _db.ScrapItems.Remove(it);
        await _db.SaveChangesAsync();
    }

    /// <summary>지난 LIST 의 MAT ID → MAT DESC(가장 최근 것). 줄 입력 때 품명을 채운다.</summary>
    public async Task<IReadOnlyList<ScrapMaterialDto>> GetScrapMaterialsAsync()
        => (await _db.ScrapItems.AsNoTracking().Where(i => i.MatId != "" && i.MatDesc != "")
                .Select(i => new { i.MatId, i.MatDesc, i.Id }).ToListAsync())
            .GroupBy(i => i.MatId).Select(g => new ScrapMaterialDto(g.Key, g.OrderByDescending(x => x.Id).First().MatDesc))
            .OrderBy(m => m.MatId).ToList();

    /// <summary>S/N·OUT NO·MAT DESC 로 모든 LIST 와 눈관리 요청에서 찾는다(최근 것부터 300줄까지).</summary>
    public async Task<ScrapSearchDto> SearchScrapAsync(string? q)
    {
        var t = (q ?? "").Trim();
        if (t.Length < 2) throw new BusinessRuleException("두 글자 이상 입력하세요.");
        var items = await _db.ScrapItems.AsNoTracking().Include(i => i.Batch)
            .Where(i => i.SerialNo.Contains(t) || i.OutNo.Contains(t) || i.MatDesc.Contains(t) || i.MatId == t)
            .OrderByDescending(i => i.Batch!.Date).ThenBy(i => i.SortOrder).Take(300).ToListAsync();
        var tags = await _db.ScrapTags.AsNoTracking()
            .Where(x => x.SerialNo.Contains(t) || x.Item.Contains(t))
            .OrderByDescending(x => x.Date).Take(100).ToListAsync();
        return new ScrapSearchDto(items.Select(i => new ScrapHitDto(i.BatchId, i.Batch!.Date, i.Batch.IsClosed, ToDto(i))).ToList(),
            tags.Select(ToDto).ToList());
    }

    // ── 눈관리 요청 ──

    public async Task<IReadOnlyList<ScrapTagDto>> GetScrapTagsAsync()
        => (await _db.ScrapTags.AsNoTracking().OrderBy(t => t.Done).ThenByDescending(t => t.Date).ThenByDescending(t => t.Id).ToListAsync())
            .Select(ToDto).ToList();

    private static void Apply(ScrapTag t, ScrapTagSaveRequest r, string actor)
    {
        t.Date = r.Date; t.Writer = Cut(r.Writer, 50); t.Item = Cut(r.Item, 100); t.SerialNo = Cut(r.SerialNo, 60);
        t.Line = Cut(r.Line, 40); t.Circle = Cut(r.Circle, 40); t.Owner = Cut(r.Owner, 40); t.Note = Cut(r.Note, 300);
        t.Done = r.Done; t.UpdatedBy = actor; t.UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 눈관리 요청 저장(id 0 이면 새로). 라인·담당자를 비워 두고 분임조만 고르면 분임조 표에서 채운다(엑셀의 담당자 자동 입력).
    /// </summary>
    public async Task<ScrapTagDto> SaveScrapTagAsync(int id, ScrapTagSaveRequest r, string actor)
    {
        if (string.IsNullOrWhiteSpace(r.Item) && string.IsNullOrWhiteSpace(r.SerialNo)) throw new BusinessRuleException("품명이나 S/N 을 입력하세요.");
        ScrapTag t;
        if (id == 0) { t = new ScrapTag(); _db.ScrapTags.Add(t); }
        else t = await _db.ScrapTags.FirstOrDefaultAsync(x => x.Id == id) ?? throw new BusinessRuleException("눈관리 요청을 찾을 수 없습니다.");
        var circle = (r.Circle ?? "").Trim();
        if (circle.Length > 0 && (string.IsNullOrWhiteSpace(r.Line) || string.IsNullOrWhiteSpace(r.Owner)))
        {
            var c = await _db.ScrapCircles.AsNoTracking().FirstOrDefaultAsync(x => x.Name == circle);
            if (c is not null)
                r = r with { Line = string.IsNullOrWhiteSpace(r.Line) ? c.Line : r.Line, Owner = string.IsNullOrWhiteSpace(r.Owner) ? c.Owner : r.Owner };
        }
        Apply(t, r with { Writer = string.IsNullOrWhiteSpace(r.Writer) ? actor : r.Writer }, actor);
        await _db.SaveChangesAsync();
        return ToDto(t);
    }

    public async Task DeleteScrapTagAsync(int id)
    {
        if (await _db.ScrapTags.Where(x => x.Id == id).ExecuteDeleteAsync() == 0) throw new BusinessRuleException("눈관리 요청을 찾을 수 없습니다.");
    }

    public async Task<IReadOnlyList<ScrapCircleDto>> GetScrapCirclesAsync()
        => await _db.ScrapCircles.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ScrapCircleDto(c.Id, c.Name, c.Line, c.Owner)).ToListAsync();

    /// <summary>분임조 표 통째로 저장 — 보낸 목록이 곧 최종(순서 포함), 빠진 분임조는 지운다.</summary>
    public async Task<IReadOnlyList<ScrapCircleDto>> SaveScrapCirclesAsync(IReadOnlyList<ScrapCircleDto> items)
    {
        var all = await _db.ScrapCircles.ToListAsync();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var it in items ?? Array.Empty<ScrapCircleDto>())
        {
            var name = Cut(it.Name, 40);
            if (name.Length == 0) continue;
            if (!seen.Add(name)) throw new BusinessRuleException($"분임조 '{name}' 가 두 번 있습니다.");
            var c = all.FirstOrDefault(x => x.Id == it.Id && it.Id > 0) ?? all.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (c is null) { c = new ScrapCircle(); _db.ScrapCircles.Add(c); all.Add(c); }
            c.Name = name; c.Line = Cut(it.Line, 40); c.Owner = Cut(it.Owner, 40); c.SortOrder = ++order;
        }
        _db.ScrapCircles.RemoveRange(all.Where(x => !seen.Contains(x.Name)));
        await _db.SaveChangesAsync();
        return await GetScrapCirclesAsync();
    }

    /// <summary>
    /// 엑셀 가져오기. 날짜와 S/N 묶음이 같은 LIST 가 이미 있으면 건너뛴다(두 번 가져와도 겹치지 않게 —
    /// 하루에 두 번 상차한 날도 있어 날짜만으로는 가리지 않고, 이력 쪽은 라인 순으로 다시 정렬돼 있어 줄 순서는 보지 않는다).
    /// 눈관리는 (날짜, S/N, 품명) 이 같은 줄을 건너뛰고, 분임조 표는 이름으로 덮어쓴다.
    /// </summary>
    public async Task<ScrapImportResultDto> ImportScrapAsync(ScrapImportRequest req, string actor)
    {
        int batches = 0, items = 0, skippedBatches = 0, tags = 0, skippedTags = 0, circles = 0;
        static string Sig(DateOnly date, IEnumerable<string> serials)
            => $"{date:yyyy-MM-dd}|{string.Join("|", serials.OrderBy(x => x, StringComparer.Ordinal))}";
        var seen = (await _db.ScrapItems.Select(i => new { i.BatchId, i.Batch!.Date, i.SerialNo }).ToListAsync())
            .GroupBy(i => (i.BatchId, i.Date)).Select(g => Sig(g.Key.Date, g.Select(i => i.SerialNo))).ToHashSet();
        foreach (var ib in req.Batches ?? Array.Empty<ScrapImportBatch>())
        {
            var rows = (ib.Items ?? Array.Empty<ScrapItemSaveRequest>()).Where(r => !IsEmpty(r)).ToList();
            if (rows.Count == 0 || !seen.Add(Sig(ib.Date, rows.Select(r => Cut(r.SerialNo, 60))))) { skippedBatches++; continue; }
            var b = new ScrapBatch
            {
                Date = ib.Date, Title = Cut(ib.Title, 100), IsClosed = ib.IsClosed, CreatedBy = actor, CreatedAt = DateTime.Now,
                ClosedAt = ib.IsClosed ? DateTime.Now : null, ClosedBy = ib.IsClosed ? actor : "",
            };
            var order = 0;
            foreach (var r in rows)
            {
                var it = new ScrapItem { SortOrder = ++order };
                Apply(it, r, actor);
                b.Items.Add(it);
            }
            _db.ScrapBatches.Add(b);
            batches++; items += rows.Count;
        }

        var existingTags = (await _db.ScrapTags.Select(t => new { t.Date, t.SerialNo, t.Item }).ToListAsync())
            .Select(t => (t.Date, t.SerialNo, t.Item)).ToHashSet();
        foreach (var r in req.Tags ?? Array.Empty<ScrapTagSaveRequest>())
        {
            if (string.IsNullOrWhiteSpace(r.Item) && string.IsNullOrWhiteSpace(r.SerialNo)) { skippedTags++; continue; }
            var t = new ScrapTag();
            Apply(t, r, actor);
            if (!existingTags.Add((t.Date, t.SerialNo, t.Item))) { skippedTags++; continue; }
            _db.ScrapTags.Add(t);
            tags++;
        }

        var all = await _db.ScrapCircles.ToListAsync();
        var max = all.Count == 0 ? 0 : all.Max(c => c.SortOrder);
        foreach (var it in req.Circles ?? Array.Empty<ScrapCircleDto>())
        {
            var name = Cut(it.Name, 40);
            if (name.Length == 0) continue;
            var c = all.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (c is null) { c = new ScrapCircle { Name = name, SortOrder = ++max }; _db.ScrapCircles.Add(c); all.Add(c); }
            c.Line = Cut(it.Line, 40); c.Owner = Cut(it.Owner, 40);
            circles++;
        }
        await _db.SaveChangesAsync();
        return new ScrapImportResultDto(batches, items, skippedBatches, tags, skippedTags, circles);
    }
}
