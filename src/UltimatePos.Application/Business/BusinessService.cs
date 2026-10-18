using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Notifications;
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
        private readonly ISystemEventRecorder _events;
        private readonly Licensing.LicensingSettings _licensing;

        public BusinessService(IBusinessRepository repository, ICurrentUser currentUser, ITenantContext tenant, ISystemEventRecorder events,
            Licensing.LicensingSettings licensing)
        {
            _repository = repository;
            _currentUser = currentUser;
            _tenant = tenant;
            _events = events;
            _licensing = licensing;
        }

        // ---- Businesses ----

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

            _events.Record(new SystemEventDraft(SystemEventTypes.BusinessProfileUpdated, $"The business profile of {request.BusinessName.Trim()} was updated.",
                EntityType: "Business", EntityId: businessId,
                Data: new { request.IsVatRegistered, MpesaShortCode = shortCode, request.MaxDiscountPercent, KraPin = request.KraPin.Trim() }));
            return ToDto(await _repository.UpdateBusinessAsync(businessId,
                request with { KraPin = request.KraPin.Trim(), MpesaShortCode = shortCode, PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber) },
                _currentUser.UserId));
        }

        /// <summary>The signed-in business's licence position and history, for its own Settings → Licence page.</summary>
        public async Task<BusinessLicenseStatusDto> GetCurrentLicenseAsync()
        {
            var businessId = _tenant.RequireBusinessId();
            var business = await _repository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            var licenses = await _repository.GetLicensesAsync();
            return new BusinessLicenseStatusDto(
                Platform.PlatformService.ToAccessDto(Licensing.LicensePolicy.Evaluate(business, _licensing, Licensing.LicensePolicy.Today)),
                business.LicenseMaxUsers, licenses.Select(Platform.PlatformService.ToDto));
        }

        private async Task EnsureCanSeeAsync(Guid businessId)
        {
            if (_currentUser.HasPermission(ViewAllPermission) || businessId == _tenant.BusinessId)
                return;

            var userId = _currentUser.UserId ?? throw new ForbiddenException("Sign in first.");
            if (!(await _repository.GetBusinessesForUserAsync(userId)).Any(b => b.BusinessId == businessId))
                throw new NotFoundException($"Business '{businessId}' not found.");
        }

        internal static void ValidateProfile(string businessName, string kraPin, decimal? maxDiscountPercent)
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

        internal static string? NormalizeShortCode(string? shortCode)
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

        private static BusinessDto ToDto(BusinessProfile b) => Platform.PlatformService.ToBusinessDto(b);

        private static CustomerDto ToDto(Customer c) =>
            new(c.CustomerId, c.BusinessId, c.CustomerName, c.KraPin, c.ContactPerson, c.PhoneNumber, c.Email, c.PhysicalAddress, c.IsActive,
                c.PriceType, c.CreditLimit);
    }
}
