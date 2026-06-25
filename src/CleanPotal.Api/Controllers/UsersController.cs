using CleanPotal.Core.DTOs;
using CleanPotal.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanPotal.Api.Controllers;

/// <summary>사용자 계정 관리. 관리자(admin)만 접근.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll([FromQuery] bool includeResigned = false)
        => Ok(await _users.GetAllAsync(includeResigned));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> Get(int id)
    {
        var u = await _users.GetAsync(id);
        return u is null ? NotFound() : Ok(u);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] UserUpsertRequest req)
    {
        try
        {
            var dto = await _users.CreateAsync(req);
            return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, [FromBody] UserUpsertRequest req)
    {
        try
        {
            var dto = await _users.UpdateAsync(id, req);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _users.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
