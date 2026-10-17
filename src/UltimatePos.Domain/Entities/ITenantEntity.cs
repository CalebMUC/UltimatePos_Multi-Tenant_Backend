using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// A record owned by exactly one business. The DbContext filters every query to the signed-in business and stamps
    /// BusinessId on insert, so application code never sets or checks it by hand.
    /// </summary>
    public interface ITenantEntity
    {
        Guid BusinessId { get; set; }
    }
}
