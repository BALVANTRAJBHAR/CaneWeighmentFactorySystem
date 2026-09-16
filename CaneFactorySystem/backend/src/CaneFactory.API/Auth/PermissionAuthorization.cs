using System.Security.Claims;
using CaneFactory.Application.Interfaces;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CaneFactory.API.Auth;

public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "PERM:";
    public HasPermissionAttribute(string permission) : base(Prefix + permission) { }
}

public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.Prefix))
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.Prefix.Length..]))
                .Build();
        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>
/// Server-side authorization boundary: verifies the JWT permission claim AND that the account
/// is still active/not deleted (60s cached DB check). A copied URL can never bypass this.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly UserStateService _userState;
    public PermissionAuthorizationHandler(UserStateService userState) => _userState = userState;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (sub == null || !int.TryParse(sub, out var userId)) return;
        if (!context.User.HasClaim("perm", requirement.Permission)) return;
        if (!await _userState.IsActiveAsync(userId)) return;
        context.Succeed(requirement);
    }
}

public class UserStateService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    public UserStateService(AppDbContext db, IMemoryCache cache) { _db = db; _cache = cache; }

    public async Task<bool> IsActiveAsync(int userId)
    {
        return await _cache.GetOrCreateAsync($"user-active:{userId}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            // MustChangePassword blocks all permission-protected endpoints until the temporary password is changed.
            return await _db.Users.AnyAsync(u => u.Id == userId && u.Status && !u.IsDeleted && !u.MustChangePassword);
        });
    }

    public void Invalidate(int userId) => _cache.Remove($"user-active:{userId}");
}

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public int? UserId
    {
        get
        {
            var sub = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            return int.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Username => User?.FindFirstValue(ClaimTypes.Name) ?? User?.FindFirstValue("unique_name");
    public string? Role => User == null ? null : string.Join(",", User.FindAll(ClaimTypes.Role).Select(c => c.Value));
    public string? Ip => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? Device => _http.HttpContext?.Request.Headers.UserAgent.ToString();
    public bool HasPermission(string code) => User?.HasClaim("perm", code) == true;
}
