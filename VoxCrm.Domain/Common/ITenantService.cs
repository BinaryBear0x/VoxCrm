using System;

namespace VoxCrm.Domain.Common
{
    public interface ITenantService
    {
        Guid GetClinicId();
    }
}
