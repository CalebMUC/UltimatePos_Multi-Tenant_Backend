using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class RolePermission
    {
        public class RolePermission
        {
            public Guid RoleId { get; set; }
            public Role Role { get; set; } = null!;
            public Guid PermissionId { get; set; }
            public Permission Permission { get; set; } = null!;
            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public Guid? CreatedBy { get; set; }
            public DateTime? LastUpdatedAt { get; set; }
            public Guid? LastUpdatedBy { get; set; }
        }

    }
}
