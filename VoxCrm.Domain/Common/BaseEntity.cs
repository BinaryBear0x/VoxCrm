using System;
using System.Collections.Generic;
using System.Text;

namespace VoxCrm.Domain.Common
{
    public abstract class BaseEntity
    {
        public  Guid ID { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

    }


}
