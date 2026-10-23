using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Web.Auth;

public static class StaffPolicies
{
    /// <summary>Any signed-in, enabled staff member.</summary>
    public const string Staff = "Staff";

    /// <summary>Administrators only.</summary>
    public const string Admin = "Admin";
}

public static class StaffClaims
{
    public const string MustChangePassword = "stoneshotspot:must_change_password";

    public static int? StaffId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(StaffRole.Admin));

    public static ClaimsPrincipal Create(Staff staff) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, staff.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.Name, staff.Username),
        new Claim(ClaimTypes.Role, staff.Role.ToString()),
        new Claim(MustChangePassword, staff.MustChangePassword ? "1" : "0"),
    ], CookieAuthenticationDefaults.AuthenticationScheme));
}

public static class StaffAuthentication
{
    public const string CookieName = ".StonesHotspot.Staff";
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(2);

    public static IServiceCollection AddStaffAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // the LAN dashboard is plain HTTP
                options.LoginPath = "/admin/login";
                options.LogoutPath = "/admin/logout";
                options.AccessDeniedPath = "/admin/denied";
                options.ExpireTimeSpan = IdleTimeout;
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = ValidateAsync;
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(StaffPolicies.Staff, p => p.RequireAuthenticatedUser());
            options.AddPolicy(StaffPolicies.Admin, p => p.RequireAuthenticatedUser().RequireRole(nameof(StaffRole.Admin)));
        });
        services.AddSingleton<LoginThrottle>();
        services.AddScoped<CurrentStaff>();
        return services;
    }

    /// <summary>
    /// Every request: the account must still exist and be enabled; a changed role or password flag updates the
    /// cookie. Disabling a cashier therefore signs them out at their next click.
    /// </summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.StaffId();
        var db = context.HttpContext.RequestServices.GetRequiredService<HotspotDbContext>();
        var staff = id is null ? null : await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, context.HttpContext.RequestAborted);
        if (staff is null || !staff.Enabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var current = StaffClaims.Create(staff);
        var claimsChanged = context.Principal!.IsInRole(staff.Role.ToString()) == false
                            || context.Principal.FindFirstValue(StaffClaims.MustChangePassword) != current.FindFirstValue(StaffClaims.MustChangePassword);
        if (claimsChanged)
        {
            context.ReplacePrincipal(current);
            context.ShouldRenew = true;
        }
    }
}

/// <summary>The signed-in staff member as a database entity (the "actor" of audit entries).</summary>
public sealed class CurrentStaff(IHttpContextAccessor accessor, HotspotDbContext db)
{
    private Staff? _staff;

    public async Task<Staff> GetAsync(CancellationToken ct = default)
    {
        if (_staff is not null)
            return _staff;
        var id = accessor.HttpContext?.User.StaffId() ?? throw new InvalidOperationException("No staff member is signed in.");
        _staff = await db.Staff.FirstAsync(s => s.Id == id, ct);
        return _staff;
    }
}
