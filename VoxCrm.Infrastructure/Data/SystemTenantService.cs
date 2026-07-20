using VoxCrm.Domain.Common;

namespace VoxCrm.Infrastructure.Data;

internal sealed class SystemTenantService : ITenantService
{
    public Guid GetClinicId() => Guid.Empty;

    public bool IsSystemContext => true;
}
