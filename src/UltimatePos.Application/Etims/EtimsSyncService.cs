using System.Text.Json;
using System.Text.Json.Nodes;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Etims
{
    /// <summary>
    /// Reports a branch's trading to KRA through its eTIMS device, in two steps the worker repeats.
    ///
    /// Sweep: finds what happened at the branch since the last sweep (completed sales, voids, returns, every stock movement) and queues
    /// one submission per document, in the order things happened. The first sweep (and the first after the business switches eTIMS
    /// back on) also brings KRA's stock in line with the branch's stock on hand: opening
    /// stock, since KRA will not accept a sale of an item it holds no stock of. Every product is registered (once: KRA refuses to save an
    /// item twice) before anything that names it. The window reaches back a margin before the last sweep so a transaction still
    /// committing then is not missed; a document is never queued twice (SourceKey is unique per device).
    ///
    /// Send: sends due submissions, items first. KRA keeps three strict series per taxpayer (item code sequence, invoice numbers, stock
    /// in/out numbers) and accepts only the exact next number, so a document's number is taken when it is sent and kept only if KRA
    /// accepts it; when KRA names the number it expects, the device follows it and sends again. Documents name items by placeholder
    /// until then, and are sent with the code and classification KRA registered. A document waits until every item it names is
    /// registered, and a credit note until its sale is accepted. A failed send is retried with growing gaps, then Rejected for a manager
    /// to look at. Each accepted stock in/out moves KRA's balance of its items, and is followed by their stock master.
    /// </summary>
    public sealed class EtimsSyncService
    {
        private static readonly TimeSpan SweepMargin = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ItemCheckInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DependencyWait = TimeSpan.FromMinutes(1);
        // One sweep or send at a time in this process, so the worker and a manager's "run now" never number or send the same thing.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private const string ItemPlaceholder = "@item:";
        private const string ClassPlaceholder = "@class:";

        private readonly IEtimsRepository _repository;
        private readonly IEtimsClient _client;
        private readonly EtimsSettings _settings;

        public EtimsSyncService(IEtimsRepository repository, IEtimsClient client, EtimsSettings settings)
        {
            _repository = repository;
            _client = client;
            _settings = settings;
        }

        public async Task<EtimsRunResultDto> RunAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var queued = await SweepAsync(deviceId);
                var (sent, failed, waiting) = await SendDueAsync(deviceId, cancellationToken);
                return new EtimsRunResultDto(queued, sent, failed, waiting);
            }
            finally
            {
                Gate.Release();
            }
        }

        // ---------------------------------------------------------------- Sweep

        private sealed class Batch
        {
            public required EtimsDevice Device { get; init; }
            public required BusinessProfile Business { get; init; }
            public required Branch Branch { get; init; }
            public required DateTime Now { get; init; }
            public HashSet<Guid> Described { get; } = new();
            public Dictionary<Guid, string> UserNames { get; set; } = new();
            public List<EtimsSubmission> Added { get; } = new();
            public HashSet<string> Keys { get; } = new();

            public string TradeName => string.IsNullOrWhiteSpace(Business.TradingName) ? Business.BusinessName : Business.TradingName!;
            public string? Address => Branch.PhysicalAddress ?? Business.PhysicalAddress;
            public EtimsUser User(Guid? userId) => EtimsUser.From(userId is { } id && UserNames.TryGetValue(id, out var name) ? name : null);

            public EtimsSubmission Add(EtimsDocumentType type, string sourceType, Guid sourceId, string key, object request,
                IEnumerable<Guid> productIds, Guid? dependsOn = null)
            {
                var submission = new EtimsSubmission
                {
                    EtimsSubmissionId = Guid.NewGuid(), BusinessId = Device.BusinessId, BranchId = Device.BranchId,
                    EtimsDeviceId = Device.EtimsDeviceId, DocumentType = type, SourceType = sourceType, SourceId = sourceId,
                    SourceKey = key, ProductIds = productIds.Distinct().ToList(), DependsOnSubmissionId = dependsOn,
                    Endpoint = EtimsEndpoints.EndpointFor(type), RequestJson = EtimsJson.Serialize(request),
                    // Queue order is send order; microsecond steps survive Postgres.
                    CreatedAt = Now.AddTicks(10 * Added.Count), NextAttemptAt = Now,
                };
                Added.Add(submission);
                Keys.Add(key);
                return submission;
            }
        }

        /// <summary>A product as documents name it until it is sent: placeholders the send replaces with what KRA registered.</summary>
        private static EtimsItemRef Placeholder(Product product, string taxTypeCode) =>
            new($"{ItemPlaceholder}{product.ProductId}", $"{ClassPlaceholder}{product.ProductId}", product.Name, product.Barcode,
                EtimsCodes.DefaultPackagingUnit, $"@unit:{product.ProductId}", taxTypeCode);

        /// <summary>Queues everything new at the device's branch. Returns how many submissions were queued.</summary>
        public async Task<int> SweepAsync(Guid deviceId)
        {
            var device = await _repository.GetDeviceAsync(deviceId)
                ?? throw new NotFoundException($"eTIMS device '{deviceId}' not found.");
            var business = await _repository.GetBusinessAsync();
            if (!device.IsActive || !business.EtimsEnabled)
                return 0;

            var batch = new Batch
            {
                Device = device,
                Business = business,
                Branch = await _repository.GetBranchAsync(device.BranchId) ?? throw new NotFoundException($"Branch '{device.BranchId}' not found."),
                Now = DateTime.UtcNow,
            };
            // Reported from when the device started, or from when the business last switched eTIMS on, whichever is later.
            var since = business.EtimsEnabledAt is { } enabledAt && enabledAt > device.ActivatedAt ? enabledAt : device.ActivatedAt;
            var from = device.SweptUntil - SweepMargin < since ? since : device.SweptUntil - SweepMargin;

            var sales = await _repository.GetCompletedSalesAsync(device.BranchId, from, batch.Now, since);
            var voided = await _repository.GetVoidedSalesAsync(device.BranchId, from, batch.Now, since);
            var returns = await _repository.GetReturnsAsync(device.BranchId, from, batch.Now, since);
            var movements = (await _repository.GetStockMovementsAsync(device.BranchId, from, batch.Now, since))
                .Where(m => m.QuantityChange != 0 && EtimsCodes.StockIoTypeFor(m.MovementType, m.QuantityChange) is not null).ToList();
            var stockGroups = movements.GroupBy(m => (m.BatchId, m.MovementType)).ToList();

            var candidateKeys = sales.Select(s => SaleKey(s.SaleId))
                .Concat(voided.Select(s => VoidKey(s.SaleId)))
                .Concat(returns.Select(r => ReturnKey(r.SaleReturnId)))
                .Concat(stockGroups.Select(g => StockKey(g.Key.BatchId, g.Key.MovementType)))
                .ToList();
            var existing = candidateKeys.Count == 0 ? new HashSet<string>() : await _repository.GetExistingSourceKeysAsync(deviceId, candidateKeys);
            sales = sales.Where(s => !existing.Contains(SaleKey(s.SaleId))).ToList();
            voided = voided.Where(s => !existing.Contains(VoidKey(s.SaleId))).ToList();
            returns = returns.Where(r => !existing.Contains(ReturnKey(r.SaleReturnId))).ToList();
            stockGroups = stockGroups.Where(g => !existing.Contains(StockKey(g.Key.BatchId, g.Key.MovementType))).ToList();

            batch.UserNames = await _repository.GetUserNamesAsync(sales.Select(s => s.CreatedBy).Concat(voided.Select(s => s.LastUpdatedBy))
                .Concat(returns.Select(r => r.CreatedBy)).Concat(movements.Select(m => m.CreatedBy))
                .OfType<Guid>().Distinct().ToList());

            // Items first: the periodic full check, then whatever these documents name.
            var fullItemCheck = device.ItemsCheckedAt is null || device.ItemsCheckedAt < batch.Now - ItemCheckInterval;
            if (fullItemCheck)
                await DescribeItemsAsync(batch, null);
            // When reporting starts, and again when the business switches eTIMS back on, KRA's stock is brought in line with what the
            // branch held at that moment: the difference from what KRA believes it holds (all of it, the first time).
            var restarted = device.OpeningStockQueuedAt is null || (business.EtimsEnabledAt is { } on && on > device.OpeningStockQueuedAt);
            var opening = new List<EtimsOpeningStock>();
            if (restarted)
            {
                var held = await _repository.GetOpeningStockAsync(device.BranchId, since);
                var kra = await _repository.GetStockBalancesAsync(device.EtimsDeviceId, held.Select(o => o.Product.ProductId).ToList());
                opening = held.Select(o => o with { Quantity = o.Quantity - (kra.GetValueOrDefault(o.Product.ProductId)?.Quantity ?? 0m) })
                    .Where(o => o.Quantity != 0).ToList();
            }
            var named = sales.SelectMany(s => s.Lines.Select(l => l.ProductId))
                .Concat(voided.SelectMany(s => s.Lines.Select(l => l.ProductId)))
                .Concat(returns.SelectMany(r => r.Lines.Select(l => l.ProductId)))
                .Concat(stockGroups.SelectMany(g => g.Select(m => m.ProductId)))
                .Concat(opening.Select(o => o.Product.ProductId))
                .Where(id => !batch.Described.Contains(id)).Distinct().ToList();
            if (named.Count > 0)
                await DescribeItemsAsync(batch, named);

            if (restarted)
            {
                QueueOpeningStock(batch, opening, since);
                device.OpeningStockQueuedAt = batch.Now;
            }

            // Everything else in the order it happened. At the same moment: stock in, then the sale, then the stock it took out.
            var saleSubmissions = await _repository.GetSaleSubmissionsAsync(deviceId,
                voided.Select(s => s.SaleId).Concat(returns.Select(r => r.SaleId)).Distinct().ToList());
            var documents = new List<(DateTime At, int Order, Action Queue)>();
            documents.AddRange(sales.Select(s => (s.CreatedAt, 1, (Action)(() => saleSubmissions[s.SaleId] = QueueSale(batch, s)))));
            // A sale made before the device was active, or voided before it was reported, was never on KRA's books: nothing to credit.
            documents.AddRange(voided.Select(s => (s.LastUpdatedAt ?? batch.Now, 1, (Action)(() =>
            {
                if (saleSubmissions.TryGetValue(s.SaleId, out var original) && original.Status != EtimsSubmissionStatus.Rejected)
                    QueueVoid(batch, s, original);
            }))));
            documents.AddRange(returns.Select(r => (r.CreatedAt, 1, (Action)(() =>
            {
                if (saleSubmissions.TryGetValue(r.SaleId, out var original) && original.Status != EtimsSubmissionStatus.Rejected)
                    QueueReturn(batch, r, original);
            }))));
            documents.AddRange(stockGroups.Select(g => (g.Min(m => m.CreatedAt), g.Sum(m => m.QuantityChange) >= 0 ? 0 : 2,
                (Action)(() => QueueStockIo(batch, g.Key.MovementType, g.ToList())))));
            foreach (var document in documents.OrderBy(d => d.At).ThenBy(d => d.Order))
                document.Queue();

            device.SweptUntil = batch.Now;
            if (fullItemCheck)
                device.ItemsCheckedAt = batch.Now;
            _repository.AddSubmissions(batch.Added);
            await _repository.SaveChangesAsync();
            return batch.Added.Count;
        }

        /// <summary>
        /// Makes sure each product has an eTIMS profile and is queued for registration on the device (once). productIds null = every
        /// product. The item code itself is made when the registration is sent: its sequence is KRA's.
        /// </summary>
        private async Task DescribeItemsAsync(Batch batch, IReadOnlyCollection<Guid>? productIds)
        {
            var sources = await _repository.GetProductSourcesAsync(productIds);
            var keys = sources.Select(s => ItemKey(s.Product.ProductId)).ToList();
            var existing = keys.Count == 0 ? new HashSet<string>() : await _repository.GetExistingSourceKeysAsync(batch.Device.EtimsDeviceId, keys);
            foreach (var source in sources)
            {
                var product = source.Product;
                batch.Described.Add(product.ProductId);
                var key = ItemKey(product.ProductId);
                if (existing.Contains(key) || batch.Keys.Contains(key)) continue;

                var profile = source.Profile ?? NewProfile(product);
                var item = new EtimsItemRef($"{ItemPlaceholder}{product.ProductId}",
                    profile.ItemClassCode ?? source.CategoryClassCode ?? batch.Device.DefaultItemClassCode ?? string.Empty,
                    product.Name, product.Barcode, profile.PackagingUnitCode, profile.QuantityUnitCode,
                    EtimsCodes.TaxTypeFor(product.TaxClassification, batch.Business.IsVatRegistered));
                var request = EtimsPayloadBuilder.Item(item, EtimsCodes.ItemTypeFor(product.ItemType), profile.OriginCountryCode,
                    source.DefaultPrice, product.IsActive, EtimsUser.System);
                batch.Add(EtimsDocumentType.Item, "Product", product.ProductId, key, request, Array.Empty<Guid>());
            }
        }

        private EtimsProductProfile NewProfile(Product product)
        {
            var profile = new EtimsProductProfile
            {
                ProductId = product.ProductId,
                BusinessId = product.BusinessId,
                PackagingUnitCode = EtimsCodes.DefaultPackagingUnit,
                QuantityUnitCode = EtimsCodes.QuantityUnitFor(product.BaseUnitOfMeasure?.Symbol, product.BaseUnitOfMeasure?.Name),
                OriginCountryCode = "KE",
            };
            _repository.AddProductProfile(profile);
            return profile;
        }

        /// <summary>The tax type a sale line was charged under (its VAT rate is a snapshot), not the product's today.</summary>
        private static string TaxTypeOf(Batch batch, SaleLine line) =>
            !batch.Business.IsVatRegistered ? EtimsCodes.TaxNonVat
            : line.TaxRate == 16m ? EtimsCodes.TaxStandard
            : line.TaxRate == 8m ? EtimsCodes.TaxReduced
            : line.Product?.TaxClassification == TaxClassification.ZeroRated ? EtimsCodes.TaxZeroRated
            : EtimsCodes.TaxExempt;

        private static EtimsLine SaleLineOf(Batch batch, SaleLine line)
        {
            var gross = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var discount = Math.Max(0m, gross - line.LineTotal);
            return new EtimsLine(Placeholder(line.Product, TaxTypeOf(batch, line)), line.QuantityInBaseUnits, Math.Max(gross, line.LineTotal), discount);
        }

        private EtimsSaleDocument SaleDocument(Batch batch, Sale sale, bool creditNote, DateTime at, string? reason, IReadOnlyList<EtimsLine> lines,
            Guid? userId, string remark) =>
            // Invoice numbers are KRA's series: 0 here, set when the document is sent.
            new(0, 0, creditNote, sale.Customer?.KraPin, sale.Customer?.CustomerName, sale.Customer?.PhoneNumber,
                EtimsCodes.PaymentTypeFor(sale.PaymentMethod), at, reason, lines, batch.TradeName, batch.Address,
                batch.Device.ReceiptTopMessage, batch.Device.ReceiptBottomMessage, batch.User(userId), remark);

        private EtimsSubmission QueueSale(Batch batch, Sale sale) =>
            batch.Add(EtimsDocumentType.Sale, "Sale", sale.SaleId, SaleKey(sale.SaleId),
                EtimsPayloadBuilder.Sale(SaleDocument(batch, sale, false, sale.CreatedAt, null, sale.Lines.Select(l => SaleLineOf(batch, l)).ToList(),
                    sale.CreatedBy, sale.SaleNumber)),
                sale.Lines.Select(l => l.ProductId));

        private void QueueVoid(Batch batch, Sale sale, EtimsSubmission original) =>
            batch.Add(EtimsDocumentType.CreditNote, "SaleVoid", sale.SaleId, VoidKey(sale.SaleId),
                EtimsPayloadBuilder.Sale(SaleDocument(batch, sale, true, sale.LastUpdatedAt ?? batch.Now, EtimsCodes.RefundReasonVoid,
                    sale.Lines.Select(l => SaleLineOf(batch, l)).ToList(), sale.LastUpdatedBy, $"Void of {sale.SaleNumber}")),
                sale.Lines.Select(l => l.ProductId), original.EtimsSubmissionId);

        private void QueueReturn(Batch batch, SaleReturn saleReturn, EtimsSubmission original)
        {
            var lines = saleReturn.Lines.Select(l => new EtimsLine(Placeholder(l.SaleLine.Product, TaxTypeOf(batch, l.SaleLine)),
                l.QuantityInBaseUnits, l.Amount)).ToList();
            batch.Add(EtimsDocumentType.CreditNote, "SaleReturn", saleReturn.SaleReturnId, ReturnKey(saleReturn.SaleReturnId),
                EtimsPayloadBuilder.Sale(SaleDocument(batch, saleReturn.Sale, true, saleReturn.CreatedAt, EtimsCodes.RefundReasonReturn, lines,
                    saleReturn.CreatedBy, $"{saleReturn.ReturnNumber} against {saleReturn.Sale.SaleNumber}")),
                saleReturn.Lines.Select(l => l.ProductId), original.EtimsSubmissionId);
        }

        /// <summary>One line per product: base-unit quantity, valued at cost (VAT-inclusive, like every amount here).</summary>
        private static List<EtimsLine> StockLines(Batch batch, IEnumerable<(Product Product, decimal Quantity, decimal Value)> lines) =>
            lines.Where(l => l.Quantity != 0)
                .Select(l => new EtimsLine(Placeholder(l.Product, EtimsCodes.TaxTypeFor(l.Product.TaxClassification, batch.Business.IsVatRegistered)),
                    l.Quantity, l.Value))
                .ToList();

        /// <summary>The stock KRA is short of comes in as an adjustment (06), any it holds too much of goes out (16).</summary>
        private void QueueOpeningStock(Batch batch, IReadOnlyList<EtimsOpeningStock> differences, DateTime at)
        {
            foreach (var (incoming, type, label) in new[] { (true, "06", "in"), (false, "16", "out") })
            {
                var part = differences.Where(o => o.Quantity > 0 == incoming).ToList();
                var lines = StockLines(batch, part.Select(o => (o.Product, Math.Abs(o.Quantity), Math.Abs(o.Quantity) * o.UnitCost)));
                if (lines.Count == 0) continue;
                batch.Add(EtimsDocumentType.StockIO, "OpeningStock", batch.Device.EtimsDeviceId,
                    $"OpeningStock:{batch.Device.EtimsDeviceId}:{at:yyyyMMddHHmmssffffff}:{label}",
                    EtimsPayloadBuilder.StockIo(new EtimsStockDocument(0, 0, type, at, lines, EtimsUser.System,
                        Remark: "Stock on hand when eTIMS reporting started")),
                    part.Select(o => o.Product.ProductId));
            }
        }

        private void QueueStockIo(Batch batch, StockMovementType type, List<StockMovement> movements)
        {
            var lines = StockLines(batch, movements.GroupBy(m => m.ProductId)
                .Select(g => (g.First().Product, Math.Abs(g.Sum(m => m.QuantityChange)), g.Sum(m => Math.Abs(m.QuantityChange) * (m.UnitCost ?? 0m)))));
            if (lines.Count == 0) return;
            var first = movements[0];
            var remark = $"{first.ReferenceType} {first.Notes}".Trim();
            var doc = new EtimsStockDocument(0, 0, EtimsCodes.StockIoTypeFor(type, movements.Sum(m => m.QuantityChange))!,
                movements.Min(m => m.CreatedAt), lines, batch.User(first.CreatedBy), Remark: remark.Length > 400 ? remark[..400] : remark);
            batch.Add(EtimsDocumentType.StockIO, type.ToString(), first.BatchId, StockKey(first.BatchId, type), EtimsPayloadBuilder.StockIo(doc),
                movements.Select(m => m.ProductId));
        }

        private static string ItemKey(Guid productId) => $"Item:{productId}";
        private static string SaleKey(Guid saleId) => $"Sale:{saleId}";
        private static string VoidKey(Guid saleId) => $"SaleVoid:{saleId}";
        private static string ReturnKey(Guid saleReturnId) => $"SaleReturn:{saleReturnId}";
        private static string StockKey(Guid batchId, StockMovementType type) => $"StockIO:{batchId}:{type}";

        // ---------------------------------------------------------------- Send

        /// <summary>What KRA registered for each product on the device: the code and classification documents must repeat.</summary>
        private sealed record Registered(string ItemCode, string ItemClassCode, string QuantityUnitCode);

        /// <summary>Sends what is due. Returns (accepted, failed, still waiting for an item or a sale).</summary>
        public async Task<(int Sent, int Failed, int Waiting)> SendDueAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            var device = await _repository.GetDeviceAsync(deviceId)
                ?? throw new NotFoundException($"eTIMS device '{deviceId}' not found.");
            if (!device.IsActive || !(await _repository.GetBusinessAsync()).EtimsEnabled)
                return (0, 0, 0);

            var key = new EtimsDeviceKey(device.Tin, device.BhfId, device.CmcKey);
            var now = DateTime.UtcNow;
            var due = await _repository.GetDueSubmissionsAsync(deviceId, now, Math.Max(1, _settings.BatchSize));
            var registered = (await _repository.GetRegisteredItemsAsync(deviceId, due.SelectMany(s => s.ProductIds).Distinct().ToList()))
                .ToDictionary(r => r.Key, r => ReadRegistration(r.Value));
            var dependencies = await _repository.GetSubmissionNumbersAsync(due.Select(s => s.DependsOnSubmissionId).OfType<Guid>().Distinct().ToList());
            // Item codes are unique in the business and KRA counts them per taxpayer: carry on from the highest any branch was given.
            var itemSeries = new[] { Math.Max(device.LastItemSequence, await _repository.GetHighestItemSequenceAsync()) };

            int sent = 0, failed = 0, waiting = 0;
            foreach (var submission in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var unregistered = submission.ProductIds.Count(id => !registered.ContainsKey(id));
                if (unregistered > 0)
                {
                    Wait(submission, now, $"Waiting for {unregistered} item(s) to be registered with KRA.");
                    waiting++;
                    continue;
                }
                long? originalInvoice = null;
                if (submission.DependsOnSubmissionId is { } dependency)
                {
                    if (!dependencies.TryGetValue(dependency, out var original) || original.Status != EtimsSubmissionStatus.Sent)
                    {
                        Wait(submission, now, "Waiting for the original sale to be accepted by KRA.");
                        waiting++;
                        continue;
                    }
                    originalInvoice = original.DocumentNumber;
                }

                var (result, body, number) = await SendNumberedAsync(device, submission, registered, originalInvoice, itemSeries, key, cancellationToken);
                submission.Attempts++;
                submission.LastAttemptAt = DateTime.UtcNow;
                submission.LastUpdatedAt = submission.LastAttemptAt;
                submission.ResponseJson = result.ResponseJson;
                submission.ResultCode = result.ResultCode;
                submission.ResultMessage = Clip(result.ResultMessage ?? result.Error, 1000);

                if (result.IsSuccess)
                {
                    sent++;
                    submission.Status = EtimsSubmissionStatus.Sent;
                    submission.SentAt = submission.LastAttemptAt;
                    submission.RequestJson = body.ToJsonString(EtimsJson.Options); // what KRA holds, numbers and codes included
                    submission.DocumentNumber = number;
                    dependencies[submission.EtimsSubmissionId] = (EtimsSubmissionStatus.Sent, number);
                    await AcceptedAsync(device, submission, body, registered);
                    if (submission.DocumentType is EtimsDocumentType.Sale or EtimsDocumentType.CreditNote)
                        Sign(submission, result);
                }
                else
                {
                    failed++;
                    var giveUp = submission.Attempts >= Math.Max(1, _settings.MaxAttempts);
                    submission.Status = giveUp ? EtimsSubmissionStatus.Rejected : EtimsSubmissionStatus.Failed;
                    submission.NextAttemptAt = DateTime.UtcNow + Backoff(submission.Attempts);
                }

                // Saved after every send: a crash never sends an accepted document twice, nor reuses a number KRA took.
                await _repository.SaveChangesAsync();
            }

            if (waiting > 0)
                await _repository.SaveChangesAsync();
            return (sent, failed, waiting);
        }

        /// <summary>
        /// Fills in what is decided at send time (the number in KRA's series, the codes KRA registered) and sends. When KRA refuses the
        /// number and names the one it expects, the device's counter follows it and the document is sent once more.
        /// </summary>
        private async Task<(EtimsResult Result, JsonObject Body, long? Number)> SendNumberedAsync(EtimsDevice device, EtimsSubmission submission,
            IReadOnlyDictionary<Guid, Registered> registered, long? originalInvoice, long[] itemSeries, EtimsDeviceKey key, CancellationToken ct)
        {
            var series = submission.DocumentType switch
            {
                EtimsDocumentType.Item => EtimsCodes.Series.Item,
                EtimsDocumentType.Sale or EtimsDocumentType.CreditNote => EtimsCodes.Series.Invoice,
                EtimsDocumentType.StockIO => EtimsCodes.Series.StockAdjustment,
                _ => (EtimsCodes.Series?)null,
            };
            EtimsProductProfile? profile = null;
            if (submission.DocumentType == EtimsDocumentType.Item)
            {
                // Described as the product is now, so a classification fixed after KRA refused it goes out on the retry.
                var source = (await _repository.GetProductSourcesAsync(new[] { submission.SourceId })).SingleOrDefault();
                if (source is not null)
                {
                    profile = source.Profile ?? NewProfile(source.Product);
                    var business = await _repository.GetBusinessAsync();
                    var item = new EtimsItemRef($"{ItemPlaceholder}{source.Product.ProductId}",
                        profile.ItemClassCode ?? source.CategoryClassCode ?? device.DefaultItemClassCode ?? string.Empty,
                        source.Product.Name, source.Product.Barcode, profile.PackagingUnitCode, profile.QuantityUnitCode,
                        EtimsCodes.TaxTypeFor(source.Product.TaxClassification, business.IsVatRegistered));
                    submission.RequestJson = EtimsJson.Serialize(EtimsPayloadBuilder.Item(item, EtimsCodes.ItemTypeFor(source.Product.ItemType),
                        profile.OriginCountryCode, source.DefaultPrice, source.Product.IsActive, EtimsUser.System));
                }
            }
            var balance = submission.DocumentType == EtimsDocumentType.StockMaster
                ? (await _repository.GetStockBalancesAsync(device.EtimsDeviceId, submission.ProductIds)).GetValueOrDefault(submission.ProductIds[0])?.Quantity ?? 0m
                : 0m;

            long? Next() => series switch
            {
                // An item KRA already registered elsewhere keeps its code; a new one takes the next number in the taxpayer's series.
                EtimsCodes.Series.Item => profile?.ItemCode is null ? itemSeries[0] + 1 : null,
                EtimsCodes.Series.Invoice => device.LastInvoiceNumber + 1,
                EtimsCodes.Series.StockAdjustment => device.LastStockAdjustmentNumber + 1,
                _ => null,
            };

            var number = Next();
            JsonObject body;
            EtimsResult result;
            // At most three sends per attempt, so a series KRA contradicts itself about (its sandbox can) never loops.
            for (var send = 1; ; send++)
            {
                body = Build(submission, number, profile, registered, originalInvoice, balance);
                result = await _client.PostAsync(submission.Endpoint, key, body.ToJsonString(EtimsJson.Options), ct);
                if (result.IsSuccess || series is not { } s || number is null || send == 3)
                    break;

                if (EtimsCodes.ExpectedNext(result.Error ?? result.ResultMessage, s) is { } expected && expected != number)
                {
                    SetLast(device, itemSeries, s, expected - 1);
                    number = Next();
                }
                else if (s == EtimsCodes.Series.Invoice && result.ResultCode == EtimsCodes.InvoiceAlreadyExists)
                {
                    // KRA holds this invoice number. If it is this document (accepted before, but the reply was lost), take KRA's copy
                    // and its signature; if it is another, the number is used up.
                    var held = await new EtimsApi(_client, key).InvoiceDetailAsync(number.Value, ct);
                    if (IsSameInvoice(held, body))
                    {
                        result = held;
                        break;
                    }
                    SetLast(device, itemSeries, s, number.Value);
                    number = Next();
                }
                else
                {
                    break;
                }
            }
            if (result.IsSuccess && series is { } accepted && number is { } taken)
                SetLast(device, itemSeries, accepted, taken);
            if (submission.DocumentType == EtimsDocumentType.Item && profile is not null && result.IsSuccess && profile.ItemCode is null)
                profile.ItemCode = body["itemCd"]!.GetValue<string>();
            return (result, body, submission.DocumentType == EtimsDocumentType.Item ? null : number);
        }

        /// <summary>KRA's copy of an invoice is this document: the same receipt type, total and items.</summary>
        private static bool IsSameInvoice(EtimsResult held, JsonObject body)
        {
            if (!held.IsSuccess || held.Data is not { } data)
                return false;
            var total = decimal.TryParse(held.Find("totAmt"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : (decimal?)null;
            var heldItems = new SortedSet<string>(Values(data, "itemCd"));
            var ourItems = new SortedSet<string>(body["itemList"]!.AsArray().Select(l => l!["itemCd"]!.GetValue<string>()));
            return total == body["totAmt"]!.GetValue<decimal>() && held.Find("rcptTyCd") == body["rcptTyCd"]!.GetValue<string>()
                   && heldItems.SetEquals(ourItems);
        }

        private static IEnumerable<string> Values(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    if (property.Name == name && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;
                    else
                        foreach (var nested in Values(property.Value, name))
                            yield return nested;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in Values(item, name))
                        yield return nested;
            }
        }

        private static void SetLast(EtimsDevice device, long[] itemSeries, EtimsCodes.Series series, long value)
        {
            switch (series)
            {
                case EtimsCodes.Series.Item: device.LastItemSequence = itemSeries[0] = value; break;
                case EtimsCodes.Series.Invoice: device.LastInvoiceNumber = value; break;
                default: device.LastStockAdjustmentNumber = value; break;
            }
        }

        private static JsonObject Build(EtimsSubmission submission, long? number, EtimsProductProfile? profile,
            IReadOnlyDictionary<Guid, Registered> registered, long? originalInvoice, decimal balance)
        {
            var body = JsonNode.Parse(submission.RequestJson)!.AsObject();
            switch (submission.DocumentType)
            {
                case EtimsDocumentType.Item:
                    var itemType = body["itemTyCd"]!.GetValue<string>();
                    body["itemCd"] = profile?.ItemCode ?? EtimsCodes.ItemCode(profile?.OriginCountryCode ?? "KE", itemType,
                        body["pkgUnitCd"]!.GetValue<string>(), body["qtyUnitCd"]!.GetValue<string>(), (int)number!.Value);
                    break;
                case EtimsDocumentType.Sale or EtimsDocumentType.CreditNote:
                    body["invcNo"] = number;
                    body["receipt"]!["rptNo"] = number;
                    if (submission.DocumentType == EtimsDocumentType.CreditNote)
                        body["orgInvcNo"] = originalInvoice;
                    break;
                case EtimsDocumentType.StockIO:
                    body["sarNo"] = number;
                    break;
                case EtimsDocumentType.StockMaster:
                    body["itemCd"] = registered[submission.ProductIds[0]].ItemCode;
                    body["rsdQty"] = balance;
                    break;
            }
            if (body["itemList"] is JsonArray lines)
                foreach (var line in lines.OfType<JsonObject>())
                    if (ProductOf(line) is { } productId && registered.TryGetValue(productId, out var item))
                    {
                        line["itemCd"] = item.ItemCode;
                        line["itemClsCd"] = item.ItemClassCode;
                        line["qtyUnitCd"] = item.QuantityUnitCode;
                    }
            return body;
        }

        private static Guid? ProductOf(JsonObject line) =>
            line["itemCd"]?.GetValue<string>() is { } code && code.StartsWith(ItemPlaceholder) && Guid.TryParse(code[ItemPlaceholder.Length..], out var id) ? id : null;

        private static Registered ReadRegistration(EtimsSubmission item)
        {
            using var json = JsonDocument.Parse(item.RequestJson);
            var root = json.RootElement;
            return new Registered(root.GetProperty("itemCd").GetString()!, root.GetProperty("itemClsCd").GetString()!, root.GetProperty("qtyUnitCd").GetString()!);
        }

        /// <summary>What follows from KRA accepting a submission: a registered item, KRA's stock balance and the stock master.</summary>
        private async Task AcceptedAsync(EtimsDevice device, EtimsSubmission submission, JsonObject body, Dictionary<Guid, Registered> registered)
        {
            if (submission.DocumentType == EtimsDocumentType.Item)
            {
                registered[submission.SourceId] = new Registered(body["itemCd"]!.GetValue<string>(), body["itemClsCd"]!.GetValue<string>(),
                    body["qtyUnitCd"]!.GetValue<string>());
                return;
            }
            if (submission.DocumentType != EtimsDocumentType.StockIO)
                return;

            // KRA's balance moves by what the stock in/out says; the stock master then states it.
            var incoming = EtimsCodes.IsIncoming(body["sarTyCd"]!.GetValue<string>());
            var changes = new Dictionary<Guid, decimal>();
            foreach (var line in body["itemList"]!.AsArray().OfType<JsonObject>())
            {
                var productId = registered.FirstOrDefault(r => r.Value.ItemCode == line["itemCd"]!.GetValue<string>()).Key;
                if (productId == Guid.Empty) continue;
                var quantity = line["qty"]!.GetValue<decimal>();
                changes[productId] = changes.GetValueOrDefault(productId) + (incoming ? quantity : -quantity);
            }
            var balances = await _repository.GetStockBalancesAsync(device.EtimsDeviceId, changes.Keys.ToList());
            var now = DateTime.UtcNow;
            foreach (var (productId, change) in changes)
            {
                if (!balances.TryGetValue(productId, out var balance))
                    _repository.AddStockBalance(balance = new EtimsStockBalance
                    {
                        EtimsDeviceId = device.EtimsDeviceId, ProductId = productId, BusinessId = device.BusinessId
                    });
                balance.Quantity += change;
                balance.LastUpdatedAt = now;
            }

            _repository.AddSubmissions(changes.Keys.Select((productId, i) => new EtimsSubmission
            {
                EtimsSubmissionId = Guid.NewGuid(), BusinessId = device.BusinessId, BranchId = device.BranchId,
                EtimsDeviceId = device.EtimsDeviceId, DocumentType = EtimsDocumentType.StockMaster, SourceType = "Product",
                SourceId = productId, SourceKey = $"StockMaster:{submission.EtimsSubmissionId}:{productId}", ProductIds = new() { productId },
                Endpoint = EtimsEndpoints.StockMaster, CreatedAt = now.AddTicks(10 * i), NextAttemptAt = now,
                // itemCd and rsdQty are filled in when it is sent: KRA's balance then, not now.
                RequestJson = EtimsJson.Serialize(EtimsPayloadBuilder.StockMaster($"{ItemPlaceholder}{productId}", 0m, EtimsUser.System)),
            }).ToList());
        }

        /// <summary>1, 2, 4 ... minutes between attempts, at most an hour.</summary>
        public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempts - 1))));

        private static void Wait(EtimsSubmission submission, DateTime now, string reason)
        {
            submission.NextAttemptAt = now + DependencyWait;
            submission.ResultMessage = reason;
            submission.LastUpdatedAt = now;
        }

        private static void Sign(EtimsSubmission submission, EtimsResult result)
        {
            submission.ReceiptNumber = long.TryParse(result.Find("curRcptNo", "rcptNo"), out var receipt) ? receipt : null;
            submission.TotalReceiptNumber = long.TryParse(result.Find("totRcptNo"), out var total) ? total : null;
            submission.InternalData = result.Find("intrlData");
            submission.ReceiptSignature = result.Find("rcptSign");
            submission.SdcDateTime = result.Find("sdcDateTime", "vsdcRcptPbctDate");
        }

        private static string? Clip(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
    }
}
