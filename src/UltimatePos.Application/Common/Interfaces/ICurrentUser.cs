using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Common.Interfaces
{
    public interface ICurrentUser
    {
        Guid? UserId { get; }
        bool IsAuthenticated { get; }
        /// <summary>The business the access token was issued for. Null when the user is signed in without one (platform-only access).</summary>
        Guid? BusinessId { get; }
        /// <summary>The branch the access token was issued for (where the user is working).</summary>
        Guid? BranchId { get; }
        /// <summary>The branches the user is assigned to in the business. Users with Branches.AccessAll may use every branch regardless.</summary>
        IReadOnlyList<Guid> BranchIds { get; }
        bool HasPermission(string permission);
    }
}
