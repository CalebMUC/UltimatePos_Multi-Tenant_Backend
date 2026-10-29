using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Etims;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Etims
{
    public class EtimsRepository : IEtimsRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly ITenantContext _tenant;

        public EtimsRepository(UltimatePosDbContext context, ITenantContext tenant)
        {
            _context = context;
            _tenant = tenant;
        }

        // ---- Devices ----

        public Task<List<EtimsDevice>> GetDevicesAsync() =>
            _context.EtimsDevices.Include(d => d.Branch).OrderBy(d => d.BhfId).ToListAsync();

        public Task<EtimsDevice?> GetDeviceAsync(Guid deviceId) =>
            _context.EtimsDevices.Include(d => d.Branch).FirstOrDefaultAsync(d => d.EtimsDeviceId == deviceId);

        public Task<bool> BranchHasDeviceAsync(Guid branchId) => _context.EtimsDevices.AnyAsync(d => d.BranchId == branchId);

        public Task<Branch?> GetBranchAsync(Guid branchId) => _context.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId);

        public Task<BusinessProfile> GetBusinessAsync()
        {
            var businessId = _tenant.RequireBusinessId();
            return _context.Businesses.SingleAsync(b => b.BusinessId == businessId);
        }

        public void AddDevice(EtimsDevice device) => _context.EtimsDevices.Add(device);

        public async Task<List<(Guid BusinessId, Guid DeviceId)>> GetActiveDevicesAnyBusinessAsync() =>
            (await _context.EtimsDevices.IgnoreQueryFilters()
                .Where(d => d.IsActive)
                .Join(_context.Businesses.IgnoreQueryFilters().Where(b => b.IsActive && !b.IsSuspended && b.EtimsEnabled), d => d.BusinessId, b => b.BusinessId, (d, b) => d)
                .Select(d => new { d.BusinessId, d.EtimsDeviceId })
                .ToListAsync())
            .Select(d => (d.BusinessId, d.EtimsDeviceId)).ToList();

        public Task<List<Branch>> GetBranchesAsync() => _context.Branches.AsNoTracking().OrderBy(b => b.BranchNumber).ToListAsync();

        // ---- The platform ----

        public async Task<List<EtimsBusinessRow>> GetBusinessesAnyBusinessAsync()
        {
            var devices = await _context.EtimsDevices.IgnoreQueryFilters().AsNoTracking()
                .GroupBy(d => d.BusinessId).Select(g => new { BusinessId = g.Key, Count = g.Count(), Active = g.Count(d => d.IsActive) })
                .ToDictionaryAsync(x => x.BusinessId);
            var submissions = (await _context.EtimsSubmissions.IgnoreQueryFilters().AsNoTracking()
                    .GroupBy(s => new { s.BusinessId, s.Status })
                    .Select(g => new { g.Key.BusinessId, g.Key.Status, Count = g.Count(), LastSentAt = g.Max(s => s.SentAt), Oldest = g.Min(s => s.CreatedAt) })
                    .ToListAsync())
                .ToLookup(x => x.BusinessId);
            var businesses = await _context.Businesses.IgnoreQueryFilters().AsNoTracking()
                .Where(b => b.EtimsEnabled || _context.EtimsDevices.IgnoreQueryFilters().Any(d => d.BusinessId == b.BusinessId))
                .OrderBy(b => b.BusinessName)
                .ToListAsync();

            return businesses.Select(b =>
            {
                var counts = submissions[b.BusinessId].ToList();
                var problems = counts.Where(c => c.Status is EtimsSubmissionStatus.Failed or EtimsSubmissionStatus.Rejected).ToList();
                var device = devices.GetValueOrDefault(b.BusinessId);
                return new EtimsBusinessRow(b.BusinessId, b.BusinessName, b.KraPin, b.EtimsEnabled, b.IsActive, b.IsSuspended,
                    device?.Count ?? 0, device?.Active ?? 0, counts.ToDictionary(c => c.Status, c => c.Count),
                    counts.Where(c => c.Status == EtimsSubmissionStatus.Sent).Select(c => c.LastSentAt).FirstOrDefault(),
                    problems.Count == 0 ? null : problems.Min(c => c.Oldest));
            }).ToList();
        }

        public async Task<PagedResult<EtimsSubmissionRow>> GetSubmissionsAnyBusinessAsync(int page, int pageSize, PlatformEtimsSubmissionQuery query)
        {
            var q = _context.EtimsSubmissions.IgnoreQueryFilters().AsNoTracking();
            if (query.BusinessId is { } businessId) q = q.Where(s => s.BusinessId == businessId);
            if (query.DeviceId is { } deviceId) q = q.Where(s => s.EtimsDeviceId == deviceId);
            q = query.Status is { } status
                ? q.Where(s => s.Status == status)
                : q.Where(s => s.Status == EtimsSubmissionStatus.Failed || s.Status == EtimsSubmissionStatus.Rejected);
            if (query.DocumentType is { } type) q = q.Where(s => s.DocumentType == type);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(s => s.LastAttemptAt ?? s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var deviceIds = items.Select(s => s.EtimsDeviceId).Distinct().ToList();
            var devices = await _context.EtimsDevices.IgnoreQueryFilters().AsNoTracking()
                .Where(d => deviceIds.Contains(d.EtimsDeviceId))
                .Select(d => new { d.EtimsDeviceId, d.Tin, d.BhfId, BranchName = d.Branch.Name })
                .ToDictionaryAsync(d => d.EtimsDeviceId);
            var businessIds = items.Select(s => s.BusinessId).Distinct().ToList();
            var names = await _context.Businesses.IgnoreQueryFilters().AsNoTracking()
                .Where(b => businessIds.Contains(b.BusinessId))
                .ToDictionaryAsync(b => b.BusinessId, b => b.BusinessName);

            var rows = items.Select(s =>
            {
                var device = devices.GetValueOrDefault(s.EtimsDeviceId);
                return new EtimsSubmissionRow(s, names.GetValueOrDefault(s.BusinessId) ?? "", device?.BranchName ?? "", device?.Tin ?? "", device?.BhfId ?? "");
            }).ToList();
            return new PagedResult<EtimsSubmissionRow>(rows, page, pageSize, total);
        }

        public Task<Guid?> GetSubmissionBusinessIdAsync(Guid submissionId) =>
            _context.EtimsSubmissions.IgnoreQueryFilters()
                .Where(s => s.EtimsSubmissionId == submissionId).Select(s => (Guid?)s.BusinessId).FirstOrDefaultAsync();

        // ---- KRA lists ----

        public async Task SaveReferenceDataAsync(Guid deviceId, string kind, string payload)
        {
            var row = await _context.EtimsReferenceData.FirstOrDefaultAsync(r => r.EtimsDeviceId == deviceId && r.Kind == kind);
            if (row is null)
                _context.EtimsReferenceData.Add(row = new EtimsReferenceData { EtimsReferenceDataId = Guid.NewGuid(), EtimsDeviceId = deviceId, Kind = kind });
            row.Payload = payload;
            row.FetchedAt = DateTime.UtcNow;
        }

        public Task<EtimsReferenceData?> GetReferenceDataAsync(Guid deviceId, string kind) =>
            _context.EtimsReferenceData.AsNoTracking().FirstOrDefaultAsync(r => r.EtimsDeviceId == deviceId && r.Kind == kind);

        // ---- Products ----

        public Task<Product?> GetProductAsync(Guid productId) =>
            _context.Products.Include(p => p.BaseUnitOfMeasure).FirstOrDefaultAsync(p => p.ProductId == productId);

        public Task<bool> CategoryExistsAsync(Guid categoryId) => _context.Categories.AnyAsync(c => c.CategoryId == categoryId);

        public Task<EtimsProductProfile?> GetProductProfileAsync(Guid productId) =>
            _context.EtimsProductProfiles.FirstOrDefaultAsync(p => p.ProductId == productId);

        public Task<EtimsCategoryProfile?> GetCategoryProfileAsync(Guid categoryId) =>
            _context.EtimsCategoryProfiles.FirstOrDefaultAsync(p => p.CategoryId == categoryId);

        public void AddProductProfile(EtimsProductProfile profile) => _context.EtimsProductProfiles.Add(profile);

        public void AddCategoryProfile(EtimsCategoryProfile profile) => _context.EtimsCategoryProfiles.Add(profile);

        public Task<bool> ItemRegisteredAnywhereAsync(Guid productId) =>
            _context.EtimsSubmissions.AnyAsync(s => s.DocumentType == EtimsDocumentType.Item && s.SourceId == productId && s.Status == EtimsSubmissionStatus.Sent);

        public async Task<long> GetHighestItemSequenceAsync() =>
            (await _context.EtimsProductProfiles.AsNoTracking().Where(p => p.ItemCode != null).Select(p => p.ItemCode!).ToListAsync())
            .Select(code => code.Length >= 7 && long.TryParse(code[^7..], out var n) ? n : 0).DefaultIfEmpty(0).Max();

        public async Task<List<EtimsProductSource>> GetProductSourcesAsync(IReadOnlyCollection<Guid>? productIds)
        {
            var query = _context.Products.Include(p => p.BaseUnitOfMeasure).AsQueryable();
            query = productIds is null
                ? query.Where(p => p.IsActive || _context.EtimsProductProfiles.Any(x => x.ProductId == p.ProductId))
                : query.Where(p => productIds.Contains(p.ProductId));
            var products = await query.ToListAsync();
            var ids = products.Select(p => p.ProductId).ToList();

            var profiles = await _context.EtimsProductProfiles.Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId);
            var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
            var categories = await _context.EtimsCategoryProfiles.AsNoTracking().Where(c => categoryIds.Contains(c.CategoryId)).ToDictionaryAsync(c => c.CategoryId);
            // The default price KRA shows for the item: the retail price per base unit in force today, else any price per base unit.
            var now = DateTime.UtcNow;
            var prices = (await _context.ProductPriceTiers.AsNoTracking()
                    .Where(t => ids.Contains(t.ProductId) && t.IsActive && t.EffectiveFrom <= now)
                    .Join(_context.Products, t => t.ProductId, p => p.ProductId, (t, p) => new { t.ProductId, t.UnitOfMeasureId, t.PriceType, t.Price, t.EffectiveFrom, p.BaseUnitOfMeasureId })
                    .Where(x => x.UnitOfMeasureId == x.BaseUnitOfMeasureId)
                    .ToListAsync())
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PriceType == PriceType.Retail ? 0 : 1).ThenByDescending(x => x.EffectiveFrom).First().Price);

            return products.Select(p => new EtimsProductSource(p, profiles.GetValueOrDefault(p.ProductId),
                categories.GetValueOrDefault(p.CategoryId)?.ItemClassCode,
                categories.GetValueOrDefault(p.CategoryId) is { } c ? c.LastUpdatedAt ?? c.CreatedAt : null,
                prices.GetValueOrDefault(p.ProductId))).ToList();
        }

        // ---- Sweep ----

        public Task<List<Sale>> GetCompletedSalesAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore) =>
            _context.Sales.AsNoTracking().AsSplitQuery()
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .Include(s => s.Customer)
                .Where(s => s.BranchId == branchId && s.Status == SaleStatus.Completed && s.CreatedAt >= notBefore
                            && ((s.CreatedAt >= from && s.CreatedAt < to) || (s.LastUpdatedAt >= from && s.LastUpdatedAt < to)))
                .ToListAsync();

        public Task<List<Sale>> GetVoidedSalesAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore) =>
            _context.Sales.AsNoTracking().AsSplitQuery()
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .Include(s => s.Customer)
                .Where(s => s.BranchId == branchId && s.Status == SaleStatus.Voided && s.CreatedAt >= notBefore
                            && s.LastUpdatedAt >= from && s.LastUpdatedAt < to)
                .ToListAsync();

        public Task<List<SaleReturn>> GetReturnsAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore) =>
            _context.SaleReturns.AsNoTracking().AsSplitQuery()
                .Include(r => r.Lines).ThenInclude(l => l.SaleLine).ThenInclude(l => l.Product)
                .Include(r => r.Sale).ThenInclude(s => s.Customer)
                .Where(r => r.BranchId == branchId && r.CreatedAt >= notBefore && r.CreatedAt >= from && r.CreatedAt < to)
                .ToListAsync();

        public Task<List<StockMovement>> GetStockMovementsAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore) =>
            _context.StockMovements.AsNoTracking().Include(m => m.Product)
                .Where(m => m.BranchId == branchId && m.CreatedAt >= notBefore && m.CreatedAt >= from && m.CreatedAt < to
                            && m.MovementType != StockMovementType.Revaluation)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

        public async Task<List<EtimsOpeningStock>> GetOpeningStockAsync(Guid branchId, DateTime at)
        {
            var levels = await _context.StockLevels.AsNoTracking().Include(l => l.Product)
                .Where(l => l.BranchId == branchId).ToListAsync();
            var since = await _context.StockMovements.AsNoTracking()
                .Where(m => m.BranchId == branchId && m.CreatedAt >= at)
                .GroupBy(m => m.ProductId).Select(g => new { ProductId = g.Key, Change = g.Sum(m => m.QuantityChange) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Change);
            return levels.Select(l => new EtimsOpeningStock(l.Product, l.QuantityOnHand - since.GetValueOrDefault(l.ProductId), l.AverageUnitCost)).ToList();
        }

        public Task<Dictionary<Guid, string>> GetUserNamesAsync(IReadOnlyCollection<Guid> userIds) =>
            _context.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.Username);

        public async Task<HashSet<string>> GetExistingSourceKeysAsync(Guid deviceId, IReadOnlyCollection<string> sourceKeys)
        {
            var found = new HashSet<string>();
            foreach (var chunk in sourceKeys.Distinct().Chunk(1000))
                found.UnionWith(await _context.EtimsSubmissions
                    .Where(s => s.EtimsDeviceId == deviceId && chunk.Contains(s.SourceKey))
                    .Select(s => s.SourceKey).ToListAsync());
            return found;
        }

        public async Task<Dictionary<Guid, EtimsSubmission>> GetSaleSubmissionsAsync(Guid deviceId, IReadOnlyCollection<Guid> saleIds) =>
            saleIds.Count == 0 ? new()
            : (await _context.EtimsSubmissions.AsNoTracking()
                .Where(s => s.EtimsDeviceId == deviceId && s.DocumentType == EtimsDocumentType.Sale && saleIds.Contains(s.SourceId))
                .ToListAsync())
            .GroupBy(s => s.SourceId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAt).First());

        public void AddSubmissions(IEnumerable<EtimsSubmission> submissions) => _context.EtimsSubmissions.AddRange(submissions);

        // ---- Sending ----

        public Task<List<EtimsSubmission>> GetDueSubmissionsAsync(Guid deviceId, DateTime now, int max) =>
            _context.EtimsSubmissions
                .Where(s => s.EtimsDeviceId == deviceId && (s.Status == EtimsSubmissionStatus.Pending || s.Status == EtimsSubmissionStatus.Failed)
                            && s.NextAttemptAt <= now)
                .OrderBy(s => s.DocumentType == EtimsDocumentType.Item ? 0 : 1).ThenBy(s => s.CreatedAt)
                .Take(max)
                .ToListAsync();

        public async Task<Dictionary<Guid, EtimsSubmission>> GetRegisteredItemsAsync(Guid deviceId, IReadOnlyCollection<Guid> productIds) =>
            productIds.Count == 0 ? new()
            : (await _context.EtimsSubmissions.AsNoTracking()
                .Where(s => s.EtimsDeviceId == deviceId && s.DocumentType == EtimsDocumentType.Item && s.Status == EtimsSubmissionStatus.Sent
                            && productIds.Contains(s.SourceId))
                .ToListAsync())
            .GroupBy(s => s.SourceId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SentAt).First());

        public async Task<Dictionary<Guid, (EtimsSubmissionStatus Status, long? DocumentNumber)>> GetSubmissionNumbersAsync(IReadOnlyCollection<Guid> submissionIds) =>
            submissionIds.Count == 0 ? new()
            : (await _context.EtimsSubmissions.Where(s => submissionIds.Contains(s.EtimsSubmissionId))
                .Select(s => new { s.EtimsSubmissionId, s.Status, s.DocumentNumber }).ToListAsync())
            .ToDictionary(s => s.EtimsSubmissionId, s => (s.Status, s.DocumentNumber));

        public async Task<Dictionary<Guid, EtimsStockBalance>> GetStockBalancesAsync(Guid deviceId, IReadOnlyCollection<Guid> productIds) =>
            productIds.Count == 0 ? new()
            : await _context.EtimsStockBalances.Where(b => b.EtimsDeviceId == deviceId && productIds.Contains(b.ProductId))
                .ToDictionaryAsync(b => b.ProductId);

        public void AddStockBalance(EtimsStockBalance balance) => _context.EtimsStockBalances.Add(balance);

        // ---- Review ----

        public async Task<PagedResult<EtimsSubmission>> GetSubmissionsAsync(int page, int pageSize, EtimsSubmissionQuery query)
        {
            var q = _context.EtimsSubmissions.AsNoTracking();
            if (query.DeviceId is { } deviceId) q = q.Where(s => s.EtimsDeviceId == deviceId);
            if (query.Status is { } status) q = q.Where(s => s.Status == status);
            if (query.DocumentType is { } type) q = q.Where(s => s.DocumentType == type);
            if (query.SourceId is { } sourceId) q = q.Where(s => s.SourceId == sourceId);
            var total = await q.CountAsync();
            var items = await q.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return new PagedResult<EtimsSubmission>(items, page, pageSize, total);
        }

        public Task<EtimsSubmission?> GetSubmissionAsync(Guid submissionId) =>
            _context.EtimsSubmissions.FirstOrDefaultAsync(s => s.EtimsSubmissionId == submissionId);

        public Task<EtimsSubmission?> GetLatestSubmissionForSourceAsync(Guid sourceId, EtimsDocumentType documentType) =>
            _context.EtimsSubmissions.AsNoTracking()
                .Where(s => s.SourceId == sourceId && s.DocumentType == documentType)
                .OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync();

        public async Task<Dictionary<EtimsSubmissionStatus, int>> CountSubmissionsAsync(Guid deviceId) =>
            await _context.EtimsSubmissions.Where(s => s.EtimsDeviceId == deviceId)
                .GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

        public Task<Customer?> GetCustomerAsync(Guid customerId) => _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
