using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Business
{
    public interface IBusinessRepository
    {
        Task<bool> KraPinExistsAsync(string kraPin, Guid? exceptBusinessId = null);
        /// <summary>Used by another business (as its short code, till or paybill), or (with <paramref name="includeBranches"/>) as any branch's own till: a short code routes to one place.</summary>
        Task<bool> MpesaShortCodeExistsAsync(string shortCode, Guid? exceptBusinessId = null, bool includeBranches = true);

        Task<IEnumerable<BusinessProfile>> GetBusinessesAsync();
        Task<IEnumerable<BusinessProfile>> GetBusinessesForUserAsync(Guid userId);
        Task<BusinessProfile?> GetBusinessByIdAsync(Guid businessId);
        Task<BusinessProfile?> GetBusinessByMpesaShortCodeAsync(string shortCode);
        Task<BusinessProfile> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request, Guid? updatedBy);
        Task<BusinessProfile> UpdateMonthlyDiscountAsync(Guid businessId, decimal? threshold, decimal? percent, MonthlyDiscountScope scope, Guid? updatedBy);
        /// <summary>Licences of the business in scope, newest first.</summary>
        Task<IReadOnlyList<BusinessLicense>> GetLicensesAsync();

        Task<Customer> CreateCustomerAsync(Customer customer);
        /// <summary>Customers of the business in scope.</summary>
        Task<IEnumerable<Customer>> GetCustomersAsync(string? search);
        Task<Customer?> GetCustomerByIdAsync(Guid customerId);
        Task<Customer> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request, Guid? updatedBy);
        Task<Customer> SetCustomerActiveStatusAsync(Guid customerId, bool isActive, Guid? updatedBy);
    }
}
