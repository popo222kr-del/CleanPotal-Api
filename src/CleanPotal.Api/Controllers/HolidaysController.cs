using System.Security.Claims;
using CleanPotal.Core;
using CleanPotal.Core.DTOs;
using CleanPotal.Core.Entities;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CleanPotal.Api.Controllers;

/// <summary>공휴일 조회(근무표/달력 표시용)와 관리자용 공휴일 관리.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HolidaysController : ControllerBase
{
    private const int MaxNameLength = 40;

    private readonly IHolidayService _holidays;
    private readonly CleanPotalDbContext _db;
    public HolidaysController(IHolidayService holidays, CleanPotalDbContext db)
    {
        _holidays = holidays;
        _db = db;
    }

    /// <summary>GET /api/holidays?year=2026</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<HolidayDto>> Get([FromQuery] int? year)
        => Ok(_holidays.GetByYear(year ?? DateTime.Today.Year));

    /// <summary>관리 화면 — 기본 목록과 관리자 수정분을 한 줄씩 나란히 보여 준다.</summary>
    [HttpGet("manage")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<HolidayManagePageDto>> Manage([FromQuery] int? year, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        var builtIn = _holidays.GetBuiltInMap(y);
        var from = new DateOnly(y, 1, 1);
        var to = new DateOnly(y, 12, 31);
        var overrides = await _db.HolidayOverrides.AsNoTracking()
            .Where(o => o.Date >= from && o.Date <= to).ToListAsync(ct);
        var byDate = overrides.ToDictionary(o => o.Date);

        var rows = new List<HolidayManageRowDto>();
        foreach (var (date, name) in builtIn)
        {
            if (!byDate.TryGetValue(date, out var o))
                rows.Add(new(date, name, "builtin", name, null, null));
            else if (!o.IsOff)
                rows.Add(new(date, name, "removed", name, o.UpdatedBy, o.UpdatedAt));
            else
                rows.Add(new(date, o.Name, o.Name == name ? "builtin" : "renamed", name, o.UpdatedBy, o.UpdatedAt));
        }
        foreach (var o in overrides.Where(o => !builtIn.ContainsKey(o.Date) && o.IsOff))
            rows.Add(new(o.Date, o.Name, "added", null, o.UpdatedBy, o.UpdatedAt));

        return Ok(new HolidayManagePageDto(y, builtIn.Count > 0, rows.OrderBy(r => r.Date).ToList()));
    }

    /// <summary>그날을 쉬는 날로 넣거나(이름 변경 포함), 기본 목록의 공휴일을 평일로 되돌린다.</summary>
    [HttpPut("manage")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<HolidayManagePageDto>> Save([FromBody] HolidaySaveRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        var builtIn = _holidays.GetBuiltInMap(req.Date.Year);
        builtIn.TryGetValue(req.Date, out var builtInName);

        if (req.IsOff)
        {
            if (name.Length == 0) throw new BusinessRuleException("공휴일 이름을 적어 주세요.");
            if (name.Length > MaxNameLength) throw new BusinessRuleException($"공휴일 이름은 {MaxNameLength}자까지입니다.");
        }
        else if (builtInName is null)
        {
            throw new BusinessRuleException("기본 목록에 없는 날은 평일로 되돌릴 것이 없습니다. 추가한 날이면 삭제하세요.");
        }

        var row = await _db.HolidayOverrides.FirstOrDefaultAsync(o => o.Date == req.Date, ct);
        // 기본 목록과 똑같이 두는 것이면 수정분을 남기지 않는다(기본 목록이 나중에 바뀌어도 따라가도록).
        if (req.IsOff && builtInName == name)
        {
            if (row is not null) _db.HolidayOverrides.Remove(row);
        }
        else
        {
            if (row is null)
            {
                row = new HolidayOverride { Date = req.Date };
                _db.HolidayOverrides.Add(row);
            }
            row.IsOff = req.IsOff;
            row.Name = req.IsOff ? name : builtInName ?? "";
            row.UpdatedAt = DateTime.Now;
            row.UpdatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
        }
        await _db.SaveChangesAsync(ct);
        _holidays.InvalidateOverrides();
        return await Manage(req.Date.Year, ct);
    }

    /// <summary>관리자 수정분을 지운다 — 그날은 기본 목록대로 돌아간다.</summary>
    [HttpDelete("manage/{date}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<ActionResult<HolidayManagePageDto>> Reset(DateOnly date, CancellationToken ct)
    {
        var row = await _db.HolidayOverrides.FirstOrDefaultAsync(o => o.Date == date, ct);
        if (row is not null)
        {
            _db.HolidayOverrides.Remove(row);
            await _db.SaveChangesAsync(ct);
            _holidays.InvalidateOverrides();
        }
        return await Manage(date.Year, ct);
    }
}
