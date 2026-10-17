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
        bool HasPermission(string permission);
    }
}
