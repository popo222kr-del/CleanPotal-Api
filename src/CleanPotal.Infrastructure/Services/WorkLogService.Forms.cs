using System.Text.RegularExpressions;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Infrastructure.Services;

/// <summary>양식 다운로드 — 웹으로 옮기지 않고 엑셀 양식 그대로 쓰는 업무 파일(4·6·10·11번 등).</summary>
public partial class WorkLogService
{
    private static WorkFormDto ToDto(WorkForm f) => new(f.Id, f.No, f.Title, f.Description, f.FileRef, f.UpdatedBy, f.UpdatedAt);

    public async Task<IReadOnlyList<WorkFormDto>> GetFormsAsync()
        => (await _db.WorkForms.AsNoTracking().OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync()).Select(ToDto).ToList();

    /// <summary>목록 통째로 저장. 파일은 첨부 보관소에 먼저 올리고 그 참조("att:번호|이름|종류")만 받는다.</summary>
    public async Task<IReadOnlyList<WorkFormDto>> SaveFormsAsync(IReadOnlyList<WorkFormSaveItem> items, string actor)
    {
        var all = await _db.WorkForms.ToListAsync();
        var keep = new HashSet<WorkForm>();
        var order = 0;
        foreach (var it in items ?? Array.Empty<WorkFormSaveItem>())
        {
            var title = Cut(it.Title, 100);
            if (title.Length == 0) throw new BusinessRuleException("양식 이름을 입력하세요.");
            var fileRef = (it.FileRef ?? "").Trim();
            if (fileRef.Length > 0 && !Regex.IsMatch(fileRef, @"^att:\d+\|")) throw new BusinessRuleException($"'{title}' 의 파일이 올바르지 않습니다. 다시 올려 주세요.");
            if (fileRef.Length > 300) throw new BusinessRuleException($"'{title}' 의 파일 이름이 너무 깁니다.");
            var f = all.FirstOrDefault(x => x.Id == it.Id && it.Id > 0);
            if (f is null) { f = new WorkForm(); _db.WorkForms.Add(f); all.Add(f); }
            var changed = f.Title != title || f.FileRef != fileRef || f.No != Cut(it.No, 10) || f.Description != Cut(it.Description, 300);
            f.No = Cut(it.No, 10); f.Title = title; f.Description = Cut(it.Description, 300); f.FileRef = fileRef;
            f.SortOrder = ++order;
            if (changed) { f.UpdatedBy = actor; f.UpdatedAt = DateTime.Now; }
            keep.Add(f);
        }
        _db.WorkForms.RemoveRange(all.Where(x => !keep.Contains(x)));
        await _db.SaveChangesAsync();
        return await GetFormsAsync();
    }
}
