using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Identity.Dtos
{
    
        public record LoginRequest(string Email, string Password);
        public record RegisterRequest(string Email, string Password, string FullName, string PhoneNumber, string RoleName);
    
        public record UserDto(Guid Id, string Email, string FullName, string PhoneNumber, IEnumerable<string> Roles, IEnumerable<string> Permissions);
        public record LoginResponseDto(string Token, DateTime ExpiresAt, UserDto User);

        public record RoleDto(Guid RoleId, string RoleName, string? Description,bool IsActive);

        public record CreateRoleRequestDto([property: Required] string RoleName, string? Description);

}

