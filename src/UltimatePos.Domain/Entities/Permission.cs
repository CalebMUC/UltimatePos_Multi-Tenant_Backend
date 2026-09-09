using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class Permission
    {
        public Guid PermissionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedBy { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime LastUpdatedBy { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    }
}
