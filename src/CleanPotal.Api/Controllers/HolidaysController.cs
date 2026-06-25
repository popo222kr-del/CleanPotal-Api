using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>공휴일 조회 API (근무표/달력 표시용).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HolidaysController : ControllerBase
{
    private readonly IHolidayService _holidays;
    public HolidaysController(IHolidayService holidays) => _holidays = holidays;

    /// <summary>GET /api/holidays?year=2026</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<HolidayDto>> Get([FromQuery] int? year)
        => Ok(_holidays.GetByYear(year ?? DateTime.Today.Year));
}
