using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using CleanPotal.Infrastructure.Data;
using CleanPotal.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>
/// 부서 목록(내 부서 표시 포함). 부서별로 따로 관리하는 자료(업체·견적서·체크시트·주간보고 등) 화면이
/// '등록 부서' 이름표와 관리자의 부서 고르기에 쓴다.
/// </summary>
[ApiController]
[Route("api/depts")]
[Authorize]
public class DeptsController : ControllerBase
{
    private readonly DeptScope _scope;
    public DeptsController(CleanPotalDbContext db, ICurrentUser me) => _scope = new DeptScope(db, me);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CalendarDeptDto>>> Get() => Ok(await _scope.ListAsync());
}
