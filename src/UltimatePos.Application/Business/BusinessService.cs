using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Helpers;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Business
{
    public class BusinessService
    {
        private readonly IBusinessRepository _repository;
        private readonly ICurrentUser _currentUser;

        public BusinessService(IBusinessRepository repository, ICurrentUser currentUser)
        {
            _repository = repository;
            _currentUser = currentUser;
        }

        public async Task<BusinessDto> RegisterBusinessAsync(RegisterBusinessRequestDto request)
        {
            if (await _repository.KraPinExistsAsync(request.KraPin))
                throw new DuplicateBusinessException($"A business with KRA PIN '{request.KraPin}' is already registered.");

            var business = new BusinessProfile
            {
                BusinessName = request.BusinessName,
                TradingName = request.TradingName,
                BusinessType = request.BusinessType,
                RegistrationNumber = request.RegistrationNumber,
                KraPin = request.KraPin,
                PhysicalAddress = request.PhysicalAddress,
                County = request.County,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                LogoUrl = request.LogoUrl,
                BackgroundImageUrl = request.BackgroundImageUrl,
                CreatedBy = _currentUser.UserId
            };

            var created = await _repository.CreateBusinessAsync(business);
            return ToDto(created);
        }
      
        public async Task<IEnumerable<BusinessDto>> GetBusinessesAsync() =>
            (await _repository.GetBusinessesAsync()).Select(ToDto);

        public async Task<BusinessDto> GetBusinessByIdAsync(Guid businessId)
        {
            var business = await _repository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            return ToDto(business);
        }

        public async Task<BusinessDto> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request) =>
            ToDto(await _repository.UpdateBusinessAsync(businessId, request, _currentUser.UserId));

        public async Task<BusinessDto> SetBusinessActiveStatusAsync(Guid businessId, bool isActive) =>
            ToDto(await _repository.SetBusinessActiveStatusAsync(businessId, isActive, _currentUser.UserId));

        public async Task<CustomerDto> RegisterCustomerAsync(RegisterCustomerRequestDto request)
        {
            if (await _repository.GetBusinessByIdAsync(request.BusinessId) is null)
                throw new NotFoundException($"Business '{request.BusinessId}' not found.");

            var customer = new Customer
            {
                BusinessId = request.BusinessId,
                CustomerName = request.CustomerName,
                KraPin = request.KraPin,
                ContactPerson = request.ContactPerson,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                PhysicalAddress = request.PhysicalAddress,
                CreatedBy = _currentUser.UserId
            };

            var created = await _repository.CreateCustomerAsync(customer);
            return ToDto(created);
        }


        public async Task<IEnumerable<CustomerDto>> GetCustomersByBusinessAsync(Guid businessId) =>
            (await _repository.GetCustomersByBusinessAsync(businessId)).Select(ToDto);

        public async Task<CustomerDto> GetCustomerByIdAsync(Guid customerId)
        {
            var customer = await _repository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            return ToDto(customer);
        }

        public async Task<CustomerDto> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request) =>
            ToDto(await _repository.UpdateCustomerAsync(customerId, request, _currentUser.UserId));

        public async Task<CustomerDto> SetCustomerActiveStatusAsync(Guid customerId, bool isActive) =>
            ToDto(await _repository.SetCustomerActiveStatusAsync(customerId, isActive, _currentUser.UserId));

        private static BusinessDto ToDto(BusinessProfile b) =>
            new(b.BusinessId, b.BusinessName, b.TradingName, b.BusinessType, b.RegistrationNumber, b.KraPin,
                b.PhysicalAddress, b.County, b.PhoneNumber, b.Email, b.LogoUrl, b.BackgroundImageUrl, b.IsActive);

        private static CustomerDto ToDto(Customer c) =>
            new(c.CustomerId, c.BusinessId, c.CustomerName, c.KraPin, c.ContactPerson, c.PhoneNumber, c.Email, c.PhysicalAddress, c.IsActive);
    }
}
