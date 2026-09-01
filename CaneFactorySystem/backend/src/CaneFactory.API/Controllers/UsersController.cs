using CaneFactory.API.Auth;
using CaneFactory.Application.Common;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly UserStateService _userState;
    private static readonly PasswordHasher<User> Hasher = new();

    public UsersController(AppDbContext db, IAuditService audit, UserStateService userState)
    {
        _db = db; _audit = audit; _userState = userState;
    }

    [HasPermission("User.View")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Where(u => !u.IsDeleted);
        if (!includeInactive) q = q.Where(u => u.Status);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.Username.Contains(search) || u.FullName.Contains(search) || u.Mobile.Contains(search));
        var total = await q.CountAsync();
        var items = await q.OrderBy(u => u.Username).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id, u.Username, u.FullName, u.Mobile, u.Email, u.Status, u.MustChangePassword,
                u.LastLoginAt, Roles = u.UserRoles.Select(r => r.Role.Name).ToList(),
                RoleIds = u.UserRoles.Select(r => r.RoleId).ToList()
            }).ToListAsync();
        return Ok(new { items, totalCount = total, page, pageSize });
    }

    [HasPermission("User.Create")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        var username = Validators.Norm(req.Username).ToLowerInvariant();
        if (username.Length < 3) return BadRequest(new { message = "Username must be at least 3 characters." });
        if (!Validators.IsMobile(req.Mobile)) return BadRequest(new { message = "Mobile must be exactly 10 digits." });
        if (!Validators.IsEmail(req.Email)) return BadRequest(new { message = "Email format is invalid." });
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == username))
            return Conflict(new { message = $"Username '{username}' already exists." });
        if (req.RoleIds.Count == 0) return BadRequest(new { message = "At least one role is required." });
        var validRoles = await _db.Roles.Where(r => req.RoleIds.Contains(r.Id) && !r.IsDeleted).ToListAsync();
        if (validRoles.Count != req.RoleIds.Count) return BadRequest(new { message = "One or more roles do not exist." });

        var user = new User
        {
            Username = username,
            FullName = Validators.Norm(req.FullName),
            Mobile = req.Mobile,
            Email = Validators.Norm(req.Email),
            MustChangePassword = true
        };
        user.PasswordHash = Hasher.HashPassword(user, req.TemporaryPassword);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        foreach (var r in validRoles) _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = r.Id });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "User", "User", user.Id.ToString(), newValue: new { user.Username, Roles = validRoles.Select(x => x.Name) });
        return Ok(new { message = $"User '{user.Username}' created successfully. Temporary password must be changed on first login.", id = user.Id });
    }

    [HasPermission("User.Edit")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(new { message = "User not found." });
        if (!Validators.IsMobile(req.Mobile)) return BadRequest(new { message = "Mobile must be exactly 10 digits." });
        if (!Validators.IsEmail(req.Email)) return BadRequest(new { message = "Email format is invalid." });

        var old = new { user.FullName, user.Mobile, user.Email, user.Status, Roles = user.UserRoles.Select(r => r.RoleId).ToList() };
        user.FullName = Validators.Norm(req.FullName);
        user.Mobile = req.Mobile;
        user.Email = Validators.Norm(req.Email);
        var deactivated = user.Status && !req.Status;
        user.Status = req.Status;
        user.UpdatedAt = DateTime.UtcNow;

        _db.UserRoles.RemoveRange(user.UserRoles);
        foreach (var rid in req.RoleIds.Distinct()) _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rid });

        if (deactivated)
        {
            var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
            foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
            _userState.Invalidate(id);
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "User", "User", id.ToString(), oldValue: old,
            newValue: new { user.FullName, user.Mobile, user.Email, user.Status, Roles = req.RoleIds });
        return Ok(new { message = $"User '{user.Username}' updated successfully." + (deactivated ? " Active sessions revoked." : "") });
    }

    [HasPermission("User.Edit")]
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> AdminResetPassword(int id, [FromBody] Dictionary<string, string> body)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(new { message = "User not found." });
        var temp = body.GetValueOrDefault("temporaryPassword") ?? "";
        if (temp.Length < 8) return BadRequest(new { message = "Temporary password must be at least 8 characters." });
        user.PasswordHash = Hasher.HashPassword(user, temp);
        user.MustChangePassword = true;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("PasswordReset", "User", "User", id.ToString(), newValue: new { By = "Admin" });
        return Ok(new { message = $"Temporary password set for '{user.Username}'. User must change it on next login." });
    }

    [HasPermission("User.Delete")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(new { message = "User not found." });
        if (user.Username == "developer") return BadRequest(new { message = "The initial developer account cannot be deleted." });
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.Status = false;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        _userState.Invalidate(id);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "User", "User", id.ToString(), oldValue: new { user.Username });
        return Ok(new { message = $"User '{user.Username}' soft-deleted. Historical records are preserved." });
    }
}

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public RolesController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HasPermission("Role.View")]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var roles = await _db.Roles.Where(r => !r.IsDeleted)
            .Select(r => new
            {
                r.Id, r.Name, r.Description, r.IsSystem,
                Permissions = r.RolePermissions.Select(rp => rp.Permission.Code).ToList()
            }).ToListAsync();
        return Ok(roles);
    }

    [HasPermission("Permission.View")]
    [HttpGet("permissions")]
    public async Task<IActionResult> Permissions() =>
        Ok(await _db.Permissions.OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new { p.Id, p.Code, p.Module, p.Action }).ToListAsync());

    [HasPermission("Role.Create")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Dictionary<string, object> body)
    {
        var name = Validators.Norm(body.GetValueOrDefault("name")?.ToString());
        if (name.Length < 3) return BadRequest(new { message = "Role name must be at least 3 characters." });
        if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower()))
            return Conflict(new { message = $"Role '{name}' already exists." });
        var role = new Role { Name = name, Description = body.GetValueOrDefault("description")?.ToString() };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Role", "Role", role.Id.ToString(), newValue: new { role.Name });
        return Ok(new { message = $"Role '{role.Name}' created successfully.", id = role.Id });
    }

    [HasPermission("Role.Edit")]
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> SetPermissions(int id, [FromBody] List<int> permissionIds)
    {
        var role = await _db.Roles.Include(r => r.RolePermissions).FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (role == null) return NotFound(new { message = "Role not found." });
        if (role.Name == "Developer") return BadRequest(new { message = "Developer role permissions cannot be reduced." });
        var old = role.RolePermissions.Select(rp => rp.PermissionId).ToList();
        _db.RolePermissions.RemoveRange(role.RolePermissions);
        var valid = await _db.Permissions.Where(p => permissionIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        foreach (var pid in valid) _db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = pid });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("PermissionChange", "Role", "Role", id.ToString(), oldValue: old, newValue: valid);
        return Ok(new { message = $"Permissions updated for role '{role.Name}'. Users receive them on next token refresh (max 15 min)." });
    }
}
