using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class User
    {
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string? PhoneNumber { get; set; }
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
        public bool IsActive { get; set; } = true;
        public bool IsLocked { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedBy { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime LastUpdatedBy { get; set; }
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    }
}
