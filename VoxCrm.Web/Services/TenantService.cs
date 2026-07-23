using System.Security.Claims;
using VoxCrm.Domain.Common;

namespace VoxCrm.Web.Services
{
    public class TenantService : ITenantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid GetClinicId()
        {
            var clinicIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("ClinicId")?.Value;
            
            if (Guid.TryParse(clinicIdClaim, out Guid clinicId))
            {
                return clinicId;
            }

            return Guid.Empty;
        }

        public bool IsSystemContext => _httpContextAccessor.HttpContext == null;
    }
}
