using System;
using System.Collections.Generic;
using System.Text;

namespace VoxCrm.Domain.Common
{
    public interface ITenantEntity
    {
        Guid ClinicID { get; set;}
    }
}
