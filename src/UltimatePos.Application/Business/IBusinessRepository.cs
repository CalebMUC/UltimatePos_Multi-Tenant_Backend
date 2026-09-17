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
        Task<bool> KraPinExistsAsync(string kraPin);
        Task<BusinessProfile> CreateBusinessAsync(BusinessProfile business);
        Task<IEnumerable<BusinessProfile>> GetBusinessesAsync();
        Task<BusinessProfile?> GetBusinessByIdAsync(Guid businessId);
        Task<BusinessProfile> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request, Guid? updatedBy);
        Task<BusinessProfile> SetBusinessActiveStatusAsync(Guid businessId, bool isActive, Guid? updatedBy);

        Task<Customer> CreateCustomerAsync(Customer customer);
        Task<IEnumerable<Customer>> GetCustomersByBusinessAsync(Guid businessId);
        Task<Customer?> GetCustomerByIdAsync(Guid customerId);
        Task<Customer> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request, Guid? updatedBy);
        Task<Customer> SetCustomerActiveStatusAsync(Guid customerId, bool isActive, Guid? updatedBy);
    }
}
