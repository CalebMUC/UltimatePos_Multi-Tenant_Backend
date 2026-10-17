using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Infrastructure.Persistence.Repositories;

public class BusinessRepository : IBusinessRepository
{
    private readonly UltimatePosDbContext _context;
    public BusinessRepository(UltimatePosDbContext context) => _context = context;

    public async Task<bool> KraPinExistsAsync(string kraPin, Guid? exceptBusinessId = null) =>
        await _context.Businesses.AnyAsync(b => b.KraPin == kraPin && (exceptBusinessId == null || b.BusinessId != exceptBusinessId));

    public async Task<bool> MpesaShortCodeExistsAsync(string shortCode, Guid? exceptBusinessId = null) =>
        await _context.Businesses.AnyAsync(b => b.MpesaShortCode == shortCode && (exceptBusinessId == null || b.BusinessId != exceptBusinessId));

    public async Task<BusinessProfile> CreateBusinessAsync(BusinessProfile business, Guid? creatorUserId)
    {
        await using var tx = await _context.Database.BeginTransactionAsync();

        _context.Businesses.Add(business);

        var businessPermissionNames = PermissionCatalog.Business.Select(p => p.Name).ToList();
        var permissions = await _context.Permissions.Where(p => businessPermissionNames.Contains(p.Name)).ToListAsync();
        var adminRole = new Role
        {
            RoleId = Guid.NewGuid(),
            BusinessId = business.BusinessId,
            RoleName = PermissionCatalog.BusinessAdminRole,
            Description = "Every business permission. Created with the business.",
            CreatedBy = creatorUserId
        };
        _context.Roles.Add(adminRole);
        _context.RolePermissions.AddRange(permissions.Select(p => new RolePermission { RoleId = adminRole.RoleId, PermissionId = p.PermissionId }));

        if (creatorUserId is { } userId && userId != Guid.Empty && await _context.Users.AnyAsync(u => u.UserId == userId))
        {
            var hasDefault = await _context.UserBusinesses.AnyAsync(m => m.UserId == userId && m.IsDefault);
            _context.UserBusinesses.Add(new UserBusiness { UserId = userId, BusinessId = business.BusinessId, IsDefault = !hasDefault, CreatedBy = userId });
            _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = adminRole.RoleId });
        }

        _context.UnitsOfMeasure.AddRange(BusinessDefaults.UnitsOfMeasure.Select(u => new UnitOfMeasure
        {
            BusinessId = business.BusinessId, Name = u.Name, Symbol = u.Symbol, CreatedBy = creatorUserId
        }));
        _context.ExpenseCategories.AddRange(BusinessDefaults.ExpenseCategories.Select(c => new ExpenseCategory
        {
            BusinessId = business.BusinessId, Code = c.Code, Name = c.Name, Description = c.Description, CreatedBy = creatorUserId
        }));

        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return business;
    }

    public async Task<IEnumerable<BusinessProfile>> GetBusinessesAsync() =>
        await _context.Businesses.AsNoTracking().OrderBy(b => b.BusinessName).ToListAsync();

    public async Task<IEnumerable<BusinessProfile>> GetBusinessesForUserAsync(Guid userId) =>
        await _context.UserBusinesses.AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Select(m => m.Business)
            .OrderBy(b => b.BusinessName)
            .ToListAsync();

    public async Task<BusinessProfile?> GetBusinessByIdAsync(Guid businessId) =>
        await _context.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.BusinessId == businessId);

    public async Task<BusinessProfile?> GetBusinessByMpesaShortCodeAsync(string shortCode) =>
        await _context.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.MpesaShortCode == shortCode && b.IsActive);

    public async Task<BusinessProfile> UpdateBusinessAsync(Guid businessId, UpdateBusinessRequestDto request, Guid? updatedBy)
    {
        var business = await _context.Businesses.FirstOrDefaultAsync(b => b.BusinessId == businessId)
            ?? throw new NotFoundException($"Business '{businessId}' not found.");

        business.BusinessName = request.BusinessName;
        business.TradingName = request.TradingName;
        business.BusinessType = request.BusinessType;
        business.RegistrationNumber = request.RegistrationNumber;
        business.KraPin = request.KraPin;
        business.PhysicalAddress = request.PhysicalAddress;
        business.County = request.County;
        business.PhoneNumber = request.PhoneNumber;
        business.Email = request.Email;
        business.LogoUrl = request.LogoUrl;
        business.BackgroundImageUrl = request.BackgroundImageUrl;
        business.IsVatRegistered = request.IsVatRegistered;
        business.MpesaShortCode = request.MpesaShortCode;
        business.MaxDiscountPercent = request.MaxDiscountPercent;
        business.LastUpdatedBy = updatedBy;
        business.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return business;
    }

    public async Task<BusinessProfile> SetBusinessActiveStatusAsync(Guid businessId, bool isActive, Guid? updatedBy)
    {
        var business = await _context.Businesses.FirstOrDefaultAsync(b => b.BusinessId == businessId)
            ?? throw new NotFoundException($"Business '{businessId}' not found.");

        business.IsActive = isActive;
        business.LastUpdatedBy = updatedBy;
        business.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return business;
    }

    public async Task<Customer> CreateCustomerAsync(Customer customer)
    {
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return customer;
    }

    public async Task<IEnumerable<Customer>> GetCustomersAsync(string? search)
    {
        var query = _context.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.CustomerName, pattern)
                                     || (c.PhoneNumber != null && EF.Functions.ILike(c.PhoneNumber, pattern))
                                     || (c.KraPin != null && EF.Functions.ILike(c.KraPin, pattern)));
        }
        return await query.OrderBy(c => c.CustomerName).ToListAsync();
    }

    public async Task<Customer?> GetCustomerByIdAsync(Guid customerId) =>
        await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);

    public async Task<Customer> UpdateCustomerAsync(Guid customerId, UpdateCustomerRequestDto request, Guid? updatedBy)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId)
            ?? throw new NotFoundException($"Customer '{customerId}' not found.");

        customer.CustomerName = request.CustomerName;
        customer.KraPin = request.KraPin;
        customer.ContactPerson = request.ContactPerson;
        customer.PhoneNumber = request.PhoneNumber;
        customer.Email = request.Email;
        customer.PhysicalAddress = request.PhysicalAddress;
        if (request.PriceType.HasValue)
            customer.PriceType = request.PriceType.Value;
        customer.CreditLimit = request.CreditLimit;
        customer.LastUpdatedBy = updatedBy;
        customer.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return customer;
    }

    public async Task<Customer> SetCustomerActiveStatusAsync(Guid customerId, bool isActive, Guid? updatedBy)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId)
            ?? throw new NotFoundException($"Customer '{customerId}' not found.");

        customer.IsActive = isActive;
        customer.LastUpdatedBy = updatedBy;
        customer.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return customer;
    }
}
