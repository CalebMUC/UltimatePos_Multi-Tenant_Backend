using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Common.Interfaces
{
    internal interface ITokenService
    {
        string GenerateToken(User user, IEnumerable<string> permissions, IEnumerable<string> roles);

    }
}
