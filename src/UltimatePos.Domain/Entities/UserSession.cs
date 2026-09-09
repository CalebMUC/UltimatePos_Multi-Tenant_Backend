using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class UserSession
    {
        public Guid UserSessionId { get; set; }
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
        public string SessionToken { get; set; } = string.Empty;
        public string DeviceInfo { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime LoginAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

    }
}
