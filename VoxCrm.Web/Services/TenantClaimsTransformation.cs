using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using VoxCrm.Domain.Entities;

namespace VoxCrm.Web.Services;

public class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly UserManager<ApplicationUser> _userManager;

    public TenantClaimsTransformation(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        if (principal.Identity is not ClaimsIdentity identity)
            return principal;

        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
            return principal;

        AddClaimIfMissing(identity, "ClinicId", user.ClinicId?.ToString());
        AddClaimIfMissing(identity, "DealerId", user.DealerId?.ToString());

        var roles = await _userManager.GetRolesAsync(user);
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
