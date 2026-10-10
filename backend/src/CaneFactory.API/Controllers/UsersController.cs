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

    private bool IsDeveloper => (_currentRoleNames()).Contains("Developer", StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> _currentRoleNames() =>
        (HttpContext.User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value));

    private async Task<IActionResult> RejectDeveloperRoleAttemptAsync(string operation, string? targetUserId, IEnumerable<int> roleIds)
    {
        await _audit.LogAsync("PrivilegeEscalationRejected", "User", "User", targetUserId,
            newValue: new { Operation = operation, RequestedRoleIds = roleIds }, success: false,
            failureReason: "Only a Developer may create, assign, or manage the Developer role.");
        return StatusCode(403, new { message = "Only a Developer may create, assign, or manage the Developer role." });
    }

    private Task<bool> IsDeveloperUserAsync(int userId) =>
        _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.Role.Name == "Developer");

    [HasPermission("User.View")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? category,
        [FromQuery] string? role, [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        // The system Developer account is hidden from Admin and every other
        // role. A signed-in Developer may see the identity for diagnostics,
        // but it remains reserved and cannot be edited from User Management.
        var q = _db.Users.Where(u => !u.IsDeleted);
        if (!IsDeveloper)
            q = q.Where(u => !u.UserRoles.Any(ur => ur.Role.Name == "Developer"));
        if (!includeInactive) q = q.Where(u => u.Status);
        var normalizedCategory = category?.Trim().ToUpperInvariant();
        if (normalizedCategory == "SYSTEM")
            q = q.Where(u => !u.UserRoles.Any(ur => ur.Role.Name == "Farmer"));
        else if (normalizedCategory == "FARMER")
            // Auto-provisioned Grower identities that have never signed in are
            // intentionally omitted. Once a farmer has logged in, an admin can
            // find and manage/reset that account here even after a later reset.
            q = q.Where(u => u.GrowerId.HasValue && u.LastLoginAt != null &&
                u.UserRoles.Any(ur => ur.Role.Name == "Farmer"));
        else if (normalizedCategory == "ALL")
            q = q.Where(u => !u.UserRoles.Any(ur => ur.Role.Name == "Farmer") || u.LastLoginAt != null);

        var normalizedRole = role?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedRole))
            q = q.Where(u => u.UserRoles.Any(ur => ur.Role.Name == normalizedRole));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var numericId = int.TryParse(term, out var parsedId) ? parsedId : (int?)null;
            q = q.Where(u => u.Username.Contains(term) || u.FullName.Contains(term) ||
                u.Mobile.Contains(term) || (u.Email != null && u.Email.Contains(term)) ||
                (numericId.HasValue && (u.Id == numericId.Value || u.GrowerId == numericId.Value)));
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);
        var total = await q.CountAsync();
        // Project UserRoles only once.  Projecting it separately for Roles and RoleIds made EF
        // compile two collection navigations for the same parent query, which triggers
        // MultipleCollectionIncludeWarning and can produce a cartesian result as this grows.
        // This is a read-only list endpoint, so shape the single collection in SQL and split it
        // into the existing response fields after materialisation.
        var pageRows = await q.OrderBy(u => u.Username).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                u.Id, u.GrowerId, u.Username, u.FullName, u.FullNameHi, u.Mobile, u.Email, u.Status, u.MustChangePassword,
                u.LastLoginAt,
                RoleAssignments = u.UserRoles.Select(r => new { r.RoleId, RoleName = r.Role.Name }).ToList()
            }).ToListAsync();
        var items = pageRows.Select(u => new
        {
            u.Id, u.GrowerId, u.Username, u.FullName, u.FullNameHi, u.Mobile, u.Email, u.Status, u.MustChangePassword, u.LastLoginAt,
            Roles = u.RoleAssignments.Select(r => r.RoleName).ToList(),
            RoleIds = u.RoleAssignments.Select(r => r.RoleId).ToList()
        });
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
        if (validRoles.Any(r => r.Name == "Developer"))
            return BadRequest(new { message = "Developer role is reserved for the system developer and cannot be assigned from User Management." });
        if (validRoles.Any(r => r.Name == "Farmer"))
            return BadRequest(new { message = "Farmer accounts are created automatically from the Grower master and cannot be created manually." });

        var user = new User
        {
            Username = username,
            FullName = Validators.Norm(req.FullName),
            FullNameHi = string.IsNullOrWhiteSpace(req.FullNameHi) ? null : req.FullNameHi.Trim(),
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
        if (user.GrowerId.HasValue)
            return BadRequest(new { message = "This Farmer account is managed through its linked Grower master." });
        if (!Validators.IsMobile(req.Mobile)) return BadRequest(new { message = "Mobile must be exactly 10 digits." });
        if (!Validators.IsEmail(req.Email)) return BadRequest(new { message = "Email format is invalid." });

        var validRoles = await _db.Roles.Where(r => req.RoleIds.Contains(r.Id) && !r.IsDeleted).ToListAsync();
        if (validRoles.Count != req.RoleIds.Distinct().Count()) return BadRequest(new { message = "One or more roles do not exist." });
        var targetIsDeveloper = await _db.UserRoles.AnyAsync(ur => ur.UserId == id && ur.Role.Name == "Developer");
        if (targetIsDeveloper || validRoles.Any(r => r.Name == "Developer"))
            return BadRequest(new { message = "Developer accounts and the Developer role cannot be managed from User Management." });
        if (validRoles.Any(r => r.Name == "Farmer"))
            return BadRequest(new { message = "Farmer role is assigned only by automatic Grower account provisioning." });

        var old = new { user.FullName, user.Mobile, user.Email, user.Status, Roles = user.UserRoles.Select(r => r.RoleId).ToList() };
        user.FullName = Validators.Norm(req.FullName);
        user.FullNameHi = string.IsNullOrWhiteSpace(req.FullNameHi) ? null : req.FullNameHi.Trim();
        user.Mobile = req.Mobile;
        user.Email = Validators.Norm(req.Email);
        var deactivated = user.Status && !req.Status;
        user.Status = req.Status;
        user.UpdatedAt = DateTime.UtcNow;

        _db.UserRoles.RemoveRange(user.UserRoles);
        foreach (var rid in req.RoleIds.Distinct()) _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rid });

        if (deactivated)
        {
            user.TokenVersion++;
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
        if (!IsDeveloper && await IsDeveloperUserAsync(id))
            return await RejectDeveloperRoleAttemptAsync("ResetPassword", id.ToString(), Array.Empty<int>());
        var temp = body.GetValueOrDefault("temporaryPassword") ?? "";
        if (temp.Length < 8) return BadRequest(new { message = "Temporary password must be at least 8 characters." });
        user.PasswordHash = Hasher.HashPassword(user, temp);
        user.MustChangePassword = true;
        user.TokenVersion++;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _userState.Invalidate(id);
        await _audit.LogAsync("PasswordReset", "User", "User", id.ToString(), newValue: new { By = "Admin" });
        return Ok(new { message = $"Temporary password set for '{user.Username}'. User must change it on next login." });
    }

    [HasPermission("User.Delete")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
        if (user == null) return NotFound(new { message = "User not found." });
        if (user.GrowerId.HasValue)
            return BadRequest(new { message = "Farmer accounts must be deactivated or deleted through the linked Grower master." });
        if (user.Username == "developer") return BadRequest(new { message = "The initial developer account cannot be deleted." });
        if (!IsDeveloper && await IsDeveloperUserAsync(id))
            return await RejectDeveloperRoleAttemptAsync("Delete", id.ToString(), Array.Empty<int>());
        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.Status = false;
        user.TokenVersion++;
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
    private readonly ICurrentUser _current;
    public RolesController(AppDbContext db, IAuditService audit, ICurrentUser current) { _db = db; _audit = audit; _current = current; }
    private bool IsDeveloper => (_current.Role ?? "").Split(',').Contains("Developer", StringComparer.OrdinalIgnoreCase);

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
        if (role.Name == "Developer" && !IsDeveloper)
        {
            await _audit.LogAsync("PrivilegeEscalationRejected", "Role", "Role", id.ToString(),
                newValue: new { Operation = "PermissionChange" }, success: false,
                failureReason: "Only a Developer may manage the Developer role.");
            return StatusCode(403, new { message = "Only a Developer may manage the Developer role." });
        }
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
