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
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Business
{
    public class BusinessService
    {
        /// <summary>Platform permission: see every business, not only the ones the user belongs to.</summary>
        public const string ViewAllPermission = "Businesses.ViewAll";

        private readonly IBusinessRepository _repository;
        private readonly ICurrentUser _currentUser;
        private readonly ITenantContext _tenant;

        public BusinessService(IBusinessRepository repository, ICurrentUser currentUser, ITenantContext tenant)
        {
            _repository = repository;
            _currentUser = currentUser;
            _tenant = tenant;
        }

        // ---- Businesses ----

        /// <summary>
        /// Onboards a business. The caller becomes its first member with an Admin role holding every business permission, and can
        /// switch to it straight away (POST /auth/switch-business).
        /// </summary>
        public async Task<BusinessDto> RegisterBusinessAsync(RegisterBusinessRequestDto request)
        {
            // Kind decides what the business may purchase, so it must be chosen explicitly — never defaulted.
            if (request.Kind is not { } kind || !Enum.IsDefined(kind))
                throw new ValidationException("kind", "Business kind is required (Manufacturer or Trader).");

            ValidateProfile(request.BusinessName, request.KraPin, request.MaxDiscountPercent);
            var shortCode = NormalizeShortCode(request.MpesaShortCode);

            if (await _repository.KraPinExistsAsync(request.KraPin.Trim()))
                throw new DuplicateBusinessException($"A business with KRA PIN '{request.KraPin}' is already registered.");
            if (shortCode is not null && await _repository.MpesaShortCodeExistsAsync(shortCode))
                throw new ValidationException("mpesaShortCode", $"M-Pesa short code '{shortCode}' is already used by another business.");

            var business = new BusinessProfile
            {
                BusinessId = Guid.NewGuid(),
                BusinessName = request.BusinessName.Trim(),
                TradingName = request.TradingName,
                BusinessType = request.BusinessType,
                Kind = kind,
                RegistrationNumber = request.RegistrationNumber,
                KraPin = request.KraPin.Trim(),
                PhysicalAddress = request.PhysicalAddress,
                County = request.County,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                LogoUrl = request.LogoUrl,
                BackgroundImageUrl = request.BackgroundImageUrl,
                IsVatRegistered = request.IsVatRegistered,
                MpesaShortCode = shortCode,
                MaxDiscountPercent = request.MaxDiscountPercent,
                CreatedBy = _currentUser.UserId
            };

            // Everything the new business starts with is owned by it, so it is written with the new business in scope.
            using (_tenant.Use(business.BusinessId))
            {
                var created = await _repository.CreateBusinessAsync(business, _currentUser.UserId);
                return ToDto(created);
            }
        }

        /// <summary>The businesses the caller belongs to — or every business, for platform users with Businesses.ViewAll.</summary>
        public async Task<IEnumerable<BusinessDto>> GetBusinessesAsync()
        {
            if (_currentUser.HasPermission(ViewAllPermission))
                return (await _repository.GetBusinessesAsync()).Select(ToDto);

            var userId = _currentUser.UserId ?? throw new ForbiddenException("Sign in first.");
            return (await _repository.GetBusinessesForUserAsync(userId)).Select(ToDto);
        }

        public async Task<BusinessDto> GetBusinessByIdAsync(Guid businessId)
        {
            await EnsureCanSeeAsync(businessId);
            var business = await _repository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            return ToDto(business);
        }

        /// <summary>The profile of the business signed in to (receipts, VAT, discount cap).</summary>
        public async Task<BusinessDto> GetCurrentBusinessAsync() => await GetBusinessByIdAsync(_tenant.RequireBusinessId());

        /// <summary>A business edits its own profile only: the id must be the business signed in to.</summary>
        public async Task<BusinessDto> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request)
        {
            if (businessId != _tenant.RequireBusinessId())
                throw new ForbiddenException("You can only edit the profile of the business you are signed in to.");

            ValidateProfile(request.BusinessName, request.KraPin, request.MaxDiscountPercent);
            var shortCode = NormalizeShortCode(request.MpesaShortCode);
            if (await _repository.KraPinExistsAsync(request.KraPin.Trim(), businessId))
                throw new DuplicateBusinessException($"A business with KRA PIN '{request.KraPin}' is already registered.");
            if (shortCode is not null && await _repository.MpesaShortCodeExistsAsync(shortCode, businessId))
                throw new ValidationException("mpesaShortCode", $"M-Pesa short code '{shortCode}' is already used by another business.");

            return ToDto(await _repository.UpdateBusinessAsync(businessId,
                request with { KraPin = request.KraPin.Trim(), MpesaShortCode = shortCode, PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber) },
                _currentUser.UserId));
        }

        /// <summary>Platform action (Businesses.Deactivate). A deactivated business can no longer be signed in to.</summary>
        public async Task<BusinessDto> SetBusinessActiveStatusAsync(Guid businessId, bool isActive) =>
            ToDto(await _repository.SetBusinessActiveStatusAsync(businessId, isActive, _currentUser.UserId));

        private async Task EnsureCanSeeAsync(Guid businessId)
        {
            if (_currentUser.HasPermission(ViewAllPermission) || businessId == _tenant.BusinessId)
                return;

            var userId = _currentUser.UserId ?? throw new ForbiddenException("Sign in first.");
            if (!(await _repository.GetBusinessesForUserAsync(userId)).Any(b => b.BusinessId == businessId))
                throw new NotFoundException($"Business '{businessId}' not found.");
        }

        private static void ValidateProfile(string businessName, string kraPin, decimal? maxDiscountPercent)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(businessName))
                errors["businessName"] = new[] { "Business name is required." };
            if (string.IsNullOrWhiteSpace(kraPin))
                errors["kraPin"] = new[] { "KRA PIN is required." };
            if (maxDiscountPercent is < 0 or > 100)
                errors["maxDiscountPercent"] = new[] { "Maximum discount must be between 0 and 100 percent." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
        }

        private static string? NormalizeShortCode(string? shortCode)
        {
            if (string.IsNullOrWhiteSpace(shortCode))
                return null;
            var trimmed = shortCode.Trim();
            if (!trimmed.All(char.IsDigit) || trimmed.Length is < 5 or > 10)
                throw new ValidationException("mpesaShortCode", "An M-Pesa short code is 5 to 10 digits.");
            return trimmed;
        }

        // ---- Customers of the business signed in to ----

        public async Task<CustomerDto> RegisterCustomerAsync(RegisterCustomerRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            if (request.BusinessId is { } given && given != Guid.Empty && given != businessId)
                throw new ForbiddenException("Customers can only be registered in the business you are signed in to.");

            ValidateCustomer(request.CustomerName, request.CreditLimit, request.PriceType);

            var business = await _repository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");

            var customer = new Customer
            {
                BusinessId = businessId,
                CustomerName = request.CustomerName.Trim(),
                KraPin = request.KraPin,
                ContactPerson = request.ContactPerson,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                PhysicalAddress = request.PhysicalAddress,
                PriceType = request.PriceType ?? (business.BusinessType == BusinessType.Wholesaler ? PriceType.Wholesale : PriceType.Retail),
                CreditLimit = request.CreditLimit,
                CreatedBy = _currentUser.UserId
            };

            var created = await _repository.CreateCustomerAsync(customer);
            return ToDto(created);
        }

        /// <param name="businessId">Accepted for older clients; must be the business signed in to when given.</param>
        public async Task<IEnumerable<CustomerDto>> GetCustomersAsync(Guid? businessId = null, string? search = null)
        {
            var current = _tenant.RequireBusinessId();
            if (businessId is { } given && given != Guid.Empty && given != current)
                throw new ForbiddenException("You can only list customers of the business you are signed in to.");
            return (await _repository.GetCustomersAsync(search)).Select(ToDto);
        }

        public async Task<CustomerDto> GetCustomerByIdAsync(Guid customerId)
        {
            var customer = await _repository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            return ToDto(customer);
        }

        public async Task<CustomerDto> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request)
        {
            ValidateCustomer(request.CustomerName, request.CreditLimit, request.PriceType);
            return ToDto(await _repository.UpdateCustomerAsync(customerId,
                request with { CustomerName = request.CustomerName.Trim(), PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber) },
                _currentUser.UserId));
        }

        public async Task<CustomerDto> SetCustomerActiveStatusAsync(Guid customerId, bool isActive) =>
            ToDto(await _repository.SetCustomerActiveStatusAsync(customerId, isActive, _currentUser.UserId));

        private static void ValidateCustomer(string customerName, decimal? creditLimit, PriceType? priceType)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(customerName))
                errors["customerName"] = new[] { "Customer name is required." };
            if (creditLimit is < 0)
                errors["creditLimit"] = new[] { "Credit limit cannot be negative." };
            if (priceType is { } p && !Enum.IsDefined(p))
                errors["priceType"] = new[] { "Price type must be Wholesale or Retail." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
        }

        private static BusinessDto ToDto(BusinessProfile b) =>
            new(b.BusinessId, b.BusinessName, b.TradingName, b.BusinessType, b.Kind,b.RegistrationNumber, b.KraPin,
                b.PhysicalAddress, b.County, b.PhoneNumber, b.Email, b.LogoUrl, b.BackgroundImageUrl, b.IsActive,
                b.IsVatRegistered, b.MpesaShortCode, b.MaxDiscountPercent);

        private static CustomerDto ToDto(Customer c) =>
            new(c.CustomerId, c.BusinessId, c.CustomerName, c.KraPin, c.ContactPerson, c.PhoneNumber, c.Email, c.PhysicalAddress, c.IsActive,
                c.PriceType, c.CreditLimit);
    }
}
