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
            // Giriş yapan kullanıcının Claim'lerinden (Çerez/Token) ClinicId'sini okuyoruz
            var clinicIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("ClinicId")?.Value;
            
            if (Guid.TryParse(clinicIdClaim, out Guid clinicId))
            {
                return clinicId;
            }

            // Geliştirme/Migration aşaması veya henüz giriş yapılmamışsa boş Guid döner
            return Guid.Empty;
        }
    }
}
