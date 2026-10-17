using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// A user's membership of one business. Users are global (one sign-in, one password); what they can see and do is
    /// decided per business by membership plus the roles they hold in that business.
    /// </summary>
    public class UserBusiness
    {
        public Guid UserId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>An inactive membership blocks sign-in to this business only; the user keeps access to their other businesses.</summary>
        public bool IsActive { get; set; } = true;
        /// <summary>The business signed in to when the user does not pick one.</summary>
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public User User { get; set; } = null!;
        public BusinessProfile Business { get; set; } = null!;
    }
}
