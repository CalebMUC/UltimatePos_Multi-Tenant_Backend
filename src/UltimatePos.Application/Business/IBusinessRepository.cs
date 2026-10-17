using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Business
{
    public interface IBusinessRepository
    {
        Task<bool> KraPinExistsAsync(string kraPin, Guid? exceptBusinessId = null);
        Task<bool> MpesaShortCodeExistsAsync(string shortCode, Guid? exceptBusinessId = null);

        /// <summary>
        /// Creates the business with its Admin role (every business permission), makes <paramref name="creatorUserId"/> a member with
        /// that role, and seeds the default units and expense categories — all or nothing.
        /// </summary>
        Task<BusinessProfile> CreateBusinessAsync(BusinessProfile business, Guid? creatorUserId);
        Task<IEnumerable<BusinessProfile>> GetBusinessesAsync();
        Task<IEnumerable<BusinessProfile>> GetBusinessesForUserAsync(Guid userId);
        Task<BusinessProfile?> GetBusinessByIdAsync(Guid businessId);
        Task<BusinessProfile?> GetBusinessByMpesaShortCodeAsync(string shortCode);
        Task<BusinessProfile> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request, Guid? updatedBy);
        Task<BusinessProfile> SetBusinessActiveStatusAsync(Guid businessId, bool isActive, Guid? updatedBy);

        Task<Customer> CreateCustomerAsync(Customer customer);
        /// <summary>Customers of the business in scope.</summary>
        Task<IEnumerable<Customer>> GetCustomersAsync(string? search);
        Task<Customer?> GetCustomerByIdAsync(Guid customerId);
        Task<Customer> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request, Guid? updatedBy);
        Task<Customer> SetCustomerActiveStatusAsync(Guid customerId, bool isActive, Guid? updatedBy);
    }
}
