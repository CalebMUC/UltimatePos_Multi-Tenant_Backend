using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Customers;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>Reads for customer buying insights (completed sales only, at the branches the caller can see), and the standing discount.</summary>
    public class CustomerInsightsRepository : ICustomerInsightsRepository
    {
        private readonly UltimatePosDbContext _context;

        public CustomerInsightsRepository(UltimatePosDbContext context) => _context = context;

        public async Task<IReadOnlyList<CustomerSaleRow>> GetSalesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? customerId)
        {
            var query = _context.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive);
            if (customerId.HasValue)
                query = query.Where(s => s.CustomerId == customerId.Value);
            return await query
                .Select(s => new CustomerSaleRow(s.SaleId, s.CustomerId, s.CreatedAt, s.TotalAmount, s.ReturnedAmount, s.CustomerDiscountAmount,
                    s.PaymentMethod, s.DeliveryMode))
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, DateTime>> GetFirstPurchasesAsync(IEnumerable<Guid> customerIds)
        {
            var ids = customerIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, DateTime>();
            return await _context.Sales.AsNoTracking()
                .Where(s => s.CustomerId != null && ids.Contains(s.CustomerId.Value) && s.Status == SaleStatus.Completed)
                .GroupBy(s => s.CustomerId!.Value)
                .Select(g => new { g.Key, First = g.Min(s => s.CreatedAt) })
                .ToDictionaryAsync(g => g.Key, g => g.First);
        }

        public async Task<IReadOnlyList<CustomerLineRow>> GetLinesAsync(Guid customerId, DateTime fromUtc, DateTime toUtcExclusive) =>
            await _context.SaleLines.AsNoTracking()
                .Where(l => l.Sale.CustomerId == customerId && l.Sale.Status == SaleStatus.Completed
                            && l.Sale.CreatedAt >= fromUtc && l.Sale.CreatedAt < toUtcExclusive)
                .Select(l => new CustomerLineRow(l.ProductId, l.Product.Name, l.Product.BaseUnitOfMeasure.Symbol, l.Quantity, l.QuantityReturned,
                    l.QuantityInBaseUnits, l.LineTotal))
                .ToListAsync();

        public async Task<IReadOnlyList<Customer>> GetCustomersAsync(IEnumerable<Guid> customerIds)
        {
            var ids = customerIds.Distinct().ToList();
            return await _context.Customers.AsNoTracking().Where(c => ids.Contains(c.CustomerId)).ToListAsync();
        }

        public async Task<Customer?> SetDiscountAsync(Guid customerId, decimal? discountPercent, string? note, Guid? userId)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (customer is null)
                return null;
            customer.DiscountPercent = discountPercent;
            customer.DiscountNote = note;
            customer.DiscountSetAt = DateTime.UtcNow;
            customer.DiscountSetBy = userId;
            customer.LastUpdatedAt = DateTime.UtcNow;
            customer.LastUpdatedBy = userId;
            await _context.SaveChangesAsync();
            return customer;
        }
    }
}
