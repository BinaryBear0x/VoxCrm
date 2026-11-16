using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using VoxCrm.Domain.Entities;
using VoxCrm.Application.Clinics;

namespace VoxCrm.Web.Services;

public class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IClinicManagementService _scope;

    public TenantClaimsTransformation(UserManager<ApplicationUser> userManager, IClinicManagementService scope)
    {
        _userManager = userManager;
        _scope = scope;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        if (principal.Identity is not ClaimsIdentity identity)
            return principal;

        // MFA/external cookies are intermediate identities, not application sessions.
        if (identity.AuthenticationType is "Identity.TwoFactorUserId" or "Identity.TwoFactorRememberMe" or "Identity.External")
            return principal;

        var user = await _userManager.GetUserAsync(principal);
        if (user == null || user.LoginDisabled || user.LockoutEnd > DateTimeOffset.UtcNow)
            return new ClaimsPrincipal(new ClaimsIdentity());
        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains("SystemAdmin") && !await _scope.IsUserScopeActiveAsync(user.ClinicId, user.DealerId))
            return new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var claim in identity.Claims
                     .Where(claim => claim.Type is "ClinicId" or "DealerId" || claim.Type == identity.RoleClaimType || claim.Type == ClaimTypes.Role)
                     .ToList())
        {
            identity.RemoveClaim(claim);
        }

        AddClaimIfMissing(identity, "ClinicId", user.ClinicId?.ToString());
        AddClaimIfMissing(identity, "DealerId", user.DealerId?.ToString());

        foreach (var role in roles)
        {
            AddClaimIfMissing(identity, identity.RoleClaimType, role);
            AddClaimIfMissing(identity, ClaimTypes.Role, role);
        }

        return principal;
    }

    private static void AddClaimIfMissing(ClaimsIdentity identity, string type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!identity.HasClaim(type, value))
            identity.AddClaim(new Claim(type, value));
    }
}
