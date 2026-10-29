using System.Text.Json;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Etims
{
    /// <summary>
    /// Setting up and watching eTIMS: register a branch's device with KRA (/initialize), download KRA's code and classification lists,
    /// say how products are described, review what was sent and retry what KRA refused, and read a sale's KRA signature for its receipt.
    /// The reporting itself is <see cref="EtimsSyncService"/>.
    /// </summary>
    public sealed class EtimsService
    {
        public const string CodesKind = "codes";
        public const string ItemClassesKind = "itemClasses";
        public const string NoticesKind = "notices";

        private readonly IEtimsRepository _repository;
        private readonly IEtimsClient _client;
        private readonly EtimsSyncService _sync;
        private readonly EtimsSettings _settings;
        private readonly ICurrentUser _currentUser;

        public EtimsService(IEtimsRepository repository, IEtimsClient client, EtimsSyncService sync, EtimsSettings settings, ICurrentUser currentUser)
        {
            _repository = repository;
            _client = client;
            _sync = sync;
            _settings = settings;
            _currentUser = currentUser;
        }

        // ---------------------------------------------------------------- Devices

        public async Task<IEnumerable<EtimsDeviceDto>> GetDevicesAsync()
        {
            var devices = await _repository.GetDevicesAsync();
            var result = new List<EtimsDeviceDto>();
            foreach (var device in devices)
                result.Add(ToDto(device, await _repository.CountSubmissionsAsync(device.EtimsDeviceId)));
            return result;
        }

        public async Task<EtimsDeviceDto> GetDeviceAsync(Guid deviceId)
        {
            var device = await RequireDeviceAsync(deviceId);
            return ToDto(device, await _repository.CountSubmissionsAsync(deviceId));
        }

        /// <summary>
        /// Initialises the branch's device with KRA and starts reporting the branch from now. KRA initialises a device once; for one it
        /// already initialised, pass the cmcKey it issued then — it is checked with a read before it is kept.
        /// </summary>
        public async Task<EtimsDeviceDto> RegisterDeviceAsync(RegisterEtimsDeviceRequestDto request)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.DeviceSerialNumber))
                errors["deviceSerialNumber"] = new[] { "The device serial number registered on the eTIMS portal is required." };
            if (string.IsNullOrWhiteSpace(request.DefaultItemClassCode))
                errors["defaultItemClassCode"] = new[] { "A default item classification is required (from KRA's item classification list)." };
            if (request.Tin is not null && !EtimsCodes.IsKraPin(request.Tin.Trim().ToUpperInvariant()))
                errors["tin"] = new[] { "Not a KRA PIN (a letter, nine digits, a letter)." };
            if (request.LastInvoiceNumber < 0 || request.LastStockAdjustmentNumber < 0 || request.LastItemSequence < 0)
                errors["lastInvoiceNumber"] = new[] { "Document numbers cannot be negative." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
            await RequireEnabledAsync();

            var branch = await _repository.GetBranchAsync(request.BranchId)
                ?? throw new NotFoundException($"Branch '{request.BranchId}' not found.");
            if (await _repository.BranchHasDeviceAsync(branch.BranchId))
                throw new InvalidAssignmentException($"Branch {branch.KraBranchId} {branch.Name} already has an eTIMS device.");

            var business = await _repository.GetBusinessAsync();
            var tin = (request.Tin ?? business.KraPin).Trim().ToUpperInvariant();
            var serial = request.DeviceSerialNumber.Trim();
            var init = await _client.InitializeAsync(new EtimsInitializeRequest(tin, branch.KraBranchId, serial));

            var cmcKey = init.IsSuccess ? init.Find("cmcKey") : null;
            if (cmcKey is null)
            {
                if (string.IsNullOrWhiteSpace(request.CmcKey))
                    throw new InvalidAssignmentException($"KRA did not initialise the device: {init.Describe()}. If it was initialised before, " +
                                                         "register it again with the cmcKey KRA issued then.");
                cmcKey = request.CmcKey.Trim();
                var check = await new EtimsApi(_client, new EtimsDeviceKey(tin, branch.KraBranchId, cmcKey)).NoticesAsync();
                if (!check.IsSuccess && !check.IsNoData)
                    throw new InvalidAssignmentException($"KRA refused the cmcKey given: {check.Describe()}.");
            }

            var now = DateTime.UtcNow;
            var device = new EtimsDevice
            {
                EtimsDeviceId = Guid.NewGuid(),
                BusinessId = business.BusinessId,
                BranchId = branch.BranchId,
                Tin = tin,
                BhfId = branch.KraBranchId,
                DeviceSerialNumber = serial,
                CmcKey = cmcKey,
                SdcId = init.Find("sdcId"),
                MrcNo = init.Find("mrcNo"),
                DeviceId = init.Find("dvcId"),
                TaxpayerName = init.Find("taxprNm"),
                KraBranchName = init.Find("bhfNm"),
                InitializationResponse = init.ResponseJson,
                DefaultItemClassCode = request.DefaultItemClassCode.Trim(),
                ReceiptTopMessage = request.ReceiptTopMessage,
                ReceiptBottomMessage = request.ReceiptBottomMessage,
                ActivatedAt = now,
                SweptUntil = now,
                LastInvoiceNumber = request.LastInvoiceNumber,
                LastStockAdjustmentNumber = request.LastStockAdjustmentNumber,
                LastItemSequence = request.LastItemSequence,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId,
            };
            _repository.AddDevice(device);
            await _repository.SaveChangesAsync();
            device.Branch = branch;
            return ToDto(device, new Dictionary<EtimsSubmissionStatus, int>());
        }

        public async Task<EtimsDeviceDto> UpdateDeviceAsync(Guid deviceId, UpdateEtimsDeviceRequestDto request)
        {
            await RequireEnabledAsync();
            if (string.IsNullOrWhiteSpace(request.DefaultItemClassCode))
                throw new ValidationException("defaultItemClassCode", "A default item classification is required.");
            var device = await RequireDeviceAsync(deviceId);
            device.DefaultItemClassCode = request.DefaultItemClassCode.Trim();
            device.ReceiptTopMessage = request.ReceiptTopMessage;
            device.ReceiptBottomMessage = request.ReceiptBottomMessage;
            if (request.IsActive && !device.IsActive)
            {
                // Reporting resumes from now: what happened while it was off is not sent (KRA would refuse it as late anyway).
                device.ActivatedAt = DateTime.UtcNow;
                device.SweptUntil = device.ActivatedAt;
            }
            device.IsActive = request.IsActive;
            device.LastUpdatedAt = DateTime.UtcNow;
            device.LastUpdatedBy = _currentUser.UserId;
            await _repository.SaveChangesAsync();
            return ToDto(device, await _repository.CountSubmissionsAsync(deviceId));
        }

        /// <summary>Sweeps and sends now instead of waiting for the worker.</summary>
        public async Task<EtimsRunResultDto> RunNowAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            await RequireDeviceAsync(deviceId);
            return await _sync.RunAsync(deviceId, cancellationToken);
        }

        // ---------------------------------------------------------------- KRA lists

        /// <summary>Downloads KRA's code list, item classifications and notices through the device and keeps the latest copy.</summary>
        public async Task<IEnumerable<EtimsReferenceSyncDto>> SyncReferenceDataAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            var device = await RequireDeviceAsync(deviceId);
            var api = Api(device);
            var results = new List<EtimsReferenceSyncDto>();
            foreach (var (kind, call, listName) in new (string, Func<Task<EtimsResult>>, string)[]
                     {
                         (CodesKind, () => api.CodeListAsync(null, cancellationToken), "clsList"),
                         (ItemClassesKind, () => api.ItemClassesAsync(null, cancellationToken), "itemClsList"),
                         (NoticesKind, () => api.NoticesAsync(null, cancellationToken), "noticeList"),
                     })
            {
                var result = await call();
                var count = 0;
                if (result.IsSuccess && result.Data is { } data)
                {
                    await _repository.SaveReferenceDataAsync(deviceId, kind, data.GetRawText());
                    count = data.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
                }
                results.Add(new EtimsReferenceSyncDto(kind, result.ResultCode, result.ResultMessage ?? result.Error, count));
            }
            device.LastCodeSyncAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync();
            return results;
        }

        public async Task<IEnumerable<EtimsCodeDto>> GetCodesAsync(Guid deviceId, string? codeClass)
        {
            using var document = await ReferenceDocumentAsync(deviceId, CodesKind);
            var codes = new List<EtimsCodeDto>();
            if (!document.RootElement.TryGetProperty("clsList", out var classes)) return codes;
            foreach (var cls in classes.EnumerateArray())
            {
                var clsCode = Str(cls, "cdCls") ?? "";
                if (codeClass is not null && clsCode != codeClass) continue;
                if (!cls.TryGetProperty("dtlList", out var details) || details.ValueKind != JsonValueKind.Array) continue;
                foreach (var detail in details.EnumerateArray())
                    codes.Add(new EtimsCodeDto(clsCode, Str(cls, "cdClsNm") ?? "", Str(detail, "cd") ?? "", Str(detail, "cdNm") ?? "", Str(detail, "useYn")));
            }
            return codes;
        }

        public async Task<IEnumerable<EtimsItemClassDto>> SearchItemClassesAsync(Guid deviceId, string? search, int take = 50)
        {
            using var document = await ReferenceDocumentAsync(deviceId, ItemClassesKind);
            if (!document.RootElement.TryGetProperty("itemClsList", out var list)) return Array.Empty<EtimsItemClassDto>();
            var term = search?.Trim();
            return list.EnumerateArray()
                .Select(c => new EtimsItemClassDto(Str(c, "itemClsCd") ?? "", Str(c, "itemClsNm") ?? "", Str(c, "itemClsLvl"), Str(c, "taxTyCd"), Str(c, "useYn")))
                .Where(c => string.IsNullOrEmpty(term) || c.ItemClassCode.StartsWith(term) || c.ItemClassName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Clamp(take, 1, 500))
                .ToList();
        }

        private async Task<JsonDocument> ReferenceDocumentAsync(Guid deviceId, string kind)
        {
            await RequireDeviceAsync(deviceId);
            var data = await _repository.GetReferenceDataAsync(deviceId, kind)
                ?? throw new NotFoundException("KRA's lists have not been downloaded for this device yet. Sync them first.");
            return JsonDocument.Parse(data.Payload);
        }

        // ---------------------------------------------------------------- Reads passed through from KRA

        public async Task<EtimsLookupDto> LookupAsync(Guid deviceId, string what, string? argument, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            var api = Api(await RequireDeviceAsync(deviceId));
            var result = what switch
            {
                "taxpayer" => await api.TaxpayerInfoAsync(argument, cancellationToken),
                "branches" => await api.BranchesAsync(argument, cancellationToken),
                "notices" => await api.NoticesAsync(argument, cancellationToken),
                "items" => await api.ItemsAsync(argument, cancellationToken),
                "purchases" => await api.PurchasesFromSuppliersAsync(argument, cancellationToken),
                "imports" => await api.ImportedItemsAsync(argument, cancellationToken),
                "sales" => await api.SalesAsync(argument, cancellationToken),
                "stock-moves" => await api.StockMovesAsync(argument, cancellationToken),
                "customer-pin" when !string.IsNullOrWhiteSpace(argument) => await api.CustomerPinAsync(argument.Trim().ToUpperInvariant(), cancellationToken),
                "invoice" when long.TryParse(argument, out var number) => await api.InvoiceDetailAsync(number, cancellationToken),
                _ => throw new ValidationException("what", $"Unknown lookup '{what}' (or its argument is missing)."),
            };
            return new EtimsLookupDto(result.Endpoint, result.ResultCode, result.ResultMessage, result.ResultDate, result.Data, result.Error);
        }

        /// <summary>Registers one of the business's customers with KRA for the device's branch.</summary>
        public async Task<EtimsLookupDto> SendCustomerAsync(Guid deviceId, Guid customerId, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            var device = await RequireDeviceAsync(deviceId);
            var customer = await _repository.GetCustomerAsync(customerId) ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            if (!EtimsCodes.IsKraPin(customer.KraPin))
                throw new InvalidAssignmentException($"{customer.CustomerName} has no valid KRA PIN to register.");
            var user = await CurrentUserAsync();
            var result = await Api(device).SaveCustomerAsync(new EtimsBranchCustomerRequest(customer.CustomerId.ToString("N")[..9].ToUpperInvariant(),
                customer.KraPin!, customer.CustomerName, customer.PhysicalAddress, customer.PhoneNumber, customer.Email, null,
                customer.IsActive ? "Y" : "N", null, user.Id, user.Name, user.Id, user.Name), cancellationToken);
            return Lookup(result);
        }

        public async Task<EtimsLookupDto> SendBranchUserAsync(Guid deviceId, RegisterEtimsBranchUserRequestDto request, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            var device = await RequireDeviceAsync(deviceId);
            var user = await CurrentUserAsync();
            var result = await Api(device).SaveBranchUserAsync(new EtimsBranchUserRequest(request.UserId, request.UserName, request.Password,
                request.Address, request.Contact, null, null, "Y", user.Id, user.Name, user.Id, user.Name), cancellationToken);
            return Lookup(result);
        }

        /// <summary>Tells KRA which of the business's items an imported item (from KRA's import list) was received as.</summary>
        public async Task<EtimsLookupDto> ConvertImportedItemAsync(Guid deviceId, ConvertEtimsImportedItemRequestDto request, CancellationToken cancellationToken = default)
        {
            await RequireEnabledAsync();
            var device = await RequireDeviceAsync(deviceId);
            var profile = await _repository.GetProductProfileAsync(request.ProductId);
            if (profile?.ItemCode is null)
                throw new InvalidAssignmentException("The product has not been registered with eTIMS yet.");
            var classCode = profile.ItemClassCode ?? device.DefaultItemClassCode ?? "";
            var user = await CurrentUserAsync();
            var result = await Api(device).ConvertImportedItemAsync(new EtimsImportedItemConvertedRequest(request.TaskCode, request.DeclarationDate,
                request.ItemSequence, request.HsCode, classCode, profile.ItemCode!, request.ImportItemStatusCode, request.Remark, user.Id, user.Name),
                cancellationToken);
            return Lookup(result);
        }

        // ---------------------------------------------------------------- How products are described

        public async Task<EtimsProductProfileDto> GetProductProfileAsync(Guid productId)
        {
            var product = await _repository.GetProductAsync(productId) ?? throw new NotFoundException($"Product '{productId}' not found.");
            var profile = await _repository.GetProductProfileAsync(productId);
            var categoryClass = (await _repository.GetCategoryProfileAsync(product.CategoryId))?.ItemClassCode;
            if (profile is null)
            {
                var unit = EtimsCodes.QuantityUnitFor(product.BaseUnitOfMeasure?.Symbol, product.BaseUnitOfMeasure?.Name);
                return new EtimsProductProfileDto(productId, null, null, categoryClass, EtimsCodes.DefaultPackagingUnit, unit, "KE", false);
            }
            return new EtimsProductProfileDto(productId, profile.ItemCode, profile.ItemClassCode, profile.ItemClassCode ?? categoryClass,
                profile.PackagingUnitCode, profile.QuantityUnitCode, profile.OriginCountryCode, await _repository.ItemRegisteredAnywhereAsync(productId));
        }

        /// <summary>
        /// Sets how a product is described to KRA. KRA registers an item once and refuses to save it again, so this is only possible
        /// until KRA first accepts the item; after that, a change would never reach it.
        /// </summary>
        public async Task<EtimsProductProfileDto> UpdateProductProfileAsync(Guid productId, UpdateEtimsProductProfileRequestDto request)
        {
            await RequireEnabledAsync();
            var product = await _repository.GetProductAsync(productId) ?? throw new NotFoundException($"Product '{productId}' not found.");
            var profile = await _repository.GetProductProfileAsync(productId);
            if (profile?.ItemCode is not null || await _repository.ItemRegisteredAnywhereAsync(productId))
                throw new InvalidAssignmentException("KRA has already registered this item, and does not let a registered item change.");

            var packaging = Code(request.PackagingUnitCode) ?? profile?.PackagingUnitCode ?? EtimsCodes.DefaultPackagingUnit;
            var quantity = Code(request.QuantityUnitCode) ?? profile?.QuantityUnitCode
                           ?? EtimsCodes.QuantityUnitFor(product.BaseUnitOfMeasure?.Symbol, product.BaseUnitOfMeasure?.Name);
            var origin = Code(request.OriginCountryCode) ?? profile?.OriginCountryCode ?? "KE";
            var errors = new Dictionary<string, string[]>();
            if (!EtimsCodes.IsUsableQuantityUnit(quantity))
                errors["quantityUnitCode"] = new[] { "Use a two-letter quantity unit from KRA's list (KG, BX, NO ...): KRA refuses item codes built on any other." };
            if (!EtimsCodes.IsUsableQuantityUnit(packaging))
                errors["packagingUnitCode"] = new[] { "Use a two-letter packaging unit from KRA's list (NT, BG, CT ...)." };
            if (origin.Length != 2)
                errors["originCountryCode"] = new[] { "Use the two-letter country code (KE)." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var now = DateTime.UtcNow;
            if (profile is null)
            {
                profile = new EtimsProductProfile { ProductId = productId, BusinessId = product.BusinessId, CreatedAt = now };
                _repository.AddProductProfile(profile);
            }
            profile.PackagingUnitCode = packaging;
            profile.QuantityUnitCode = quantity;
            profile.OriginCountryCode = origin;
            profile.ItemClassCode = string.IsNullOrWhiteSpace(request.ItemClassCode) ? null : request.ItemClassCode.Trim();
            profile.LastUpdatedAt = now;
            profile.LastUpdatedBy = _currentUser.UserId;
            await _repository.SaveChangesAsync();
            return await GetProductProfileAsync(productId);
        }

        /// <summary>The classification the category's products are registered with, unless their own profile names one. Products KRA
        /// already registered keep theirs.</summary>
        public async Task SetCategoryClassAsync(Guid categoryId, SetEtimsCategoryClassRequestDto request)
        {
            await RequireEnabledAsync();
            if (string.IsNullOrWhiteSpace(request.ItemClassCode))
                throw new ValidationException("itemClassCode", "An item classification is required.");
            if (!await _repository.CategoryExistsAsync(categoryId))
                throw new NotFoundException($"Category '{categoryId}' not found.");
            var profile = await _repository.GetCategoryProfileAsync(categoryId);
            var now = DateTime.UtcNow;
            if (profile is null)
            {
                profile = new EtimsCategoryProfile { CategoryId = categoryId, CreatedAt = now };
                _repository.AddCategoryProfile(profile);
            }
            profile.ItemClassCode = request.ItemClassCode.Trim();
            profile.LastUpdatedAt = now;
            profile.LastUpdatedBy = _currentUser.UserId;
            await _repository.SaveChangesAsync();
        }

        // ---------------------------------------------------------------- What was sent

        public async Task<PagedResult<EtimsSubmissionDto>> GetSubmissionsAsync(int page, int pageSize, EtimsSubmissionQuery query)
        {
            var paged = await _repository.GetSubmissionsAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, 200), query);
            return new PagedResult<EtimsSubmissionDto>(paged.Items.Select(ToDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
        }

        public async Task<EtimsSubmissionDetailDto> GetSubmissionAsync(Guid submissionId)
        {
            var s = await _repository.GetSubmissionAsync(submissionId) ?? throw new NotFoundException($"eTIMS submission '{submissionId}' not found.");
            using var request = JsonDocument.Parse(s.RequestJson);
            JsonElement? response = null;
            if (!string.IsNullOrWhiteSpace(s.ResponseJson))
            {
                try { using var r = JsonDocument.Parse(s.ResponseJson); response = r.RootElement.Clone(); }
                catch (JsonException) { response = JsonSerializer.SerializeToElement(s.ResponseJson); }
            }
            return new EtimsSubmissionDetailDto(ToDto(s), request.RootElement.Clone(), response);
        }

        /// <summary>Sends a Rejected or Failed submission again on the next pass, with a fresh set of attempts.</summary>
        public async Task<EtimsSubmissionDto> RetryAsync(Guid submissionId)
        {
            await RequireEnabledAsync();
            var s = await _repository.GetSubmissionAsync(submissionId) ?? throw new NotFoundException($"eTIMS submission '{submissionId}' not found.");
            if (s.Status == EtimsSubmissionStatus.Sent)
                throw new InvalidAssignmentException("KRA has already accepted this submission.");
            s.Status = EtimsSubmissionStatus.Pending;
            s.Attempts = 0;
            s.NextAttemptAt = DateTime.UtcNow;
            s.LastUpdatedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync();
            return ToDto(s);
        }

        /// <summary>The sale's KRA signature for its receipt. A voided sale's credit note is reported separately.</summary>
        public async Task<EtimsReceiptDto> GetSaleReceiptAsync(Guid saleId)
        {
            var s = await _repository.GetLatestSubmissionForSourceAsync(saleId, EtimsDocumentType.Sale)
                ?? throw new NotFoundException("This sale has not been queued for eTIMS (its branch has no active device, or it predates it).");
            var device = await RequireDeviceAsync(s.EtimsDeviceId);
            var url = s.ReceiptSignature is null ? null : $"{_settings.ReceiptVerificationUrl}{device.Tin}{device.BhfId}{s.ReceiptSignature}";
            return new EtimsReceiptDto(saleId, s.Status, s.DocumentNumber, s.ReceiptNumber, s.TotalReceiptNumber, s.InternalData,
                s.ReceiptSignature, device.SdcId, device.MrcNo, s.SdcDateTime, url, s.Status == EtimsSubmissionStatus.Sent ? null : s.ResultMessage);
        }

        // ----------------------------------------------------------------

        /// <summary>eTIMS is a per-business switch (set by super users): a business without it cannot set it up or send anything.</summary>
        private async Task RequireEnabledAsync()
        {
            if (!(await _repository.GetBusinessAsync()).EtimsEnabled)
                throw new InvalidAssignmentException("KRA eTIMS is not switched on for this business. Ask the platform administrator to turn it on.");
        }

        private async Task<EtimsDevice> RequireDeviceAsync(Guid deviceId) =>
            await _repository.GetDeviceAsync(deviceId) ?? throw new NotFoundException($"eTIMS device '{deviceId}' not found.");

        private async Task<EtimsUser> CurrentUserAsync() =>
            _currentUser.UserId is { } id && (await _repository.GetUserNamesAsync(new[] { id })).TryGetValue(id, out var name)
                ? EtimsUser.From(name) : EtimsUser.System;

        private EtimsApi Api(EtimsDevice device) => new(_client, new EtimsDeviceKey(device.Tin, device.BhfId, device.CmcKey));

        private static EtimsLookupDto Lookup(EtimsResult r) => new(r.Endpoint, r.ResultCode, r.ResultMessage, r.ResultDate, r.Data, r.Error);

        private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

        private static string? Str(JsonElement element, string name) =>
            element.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Null ? null : v.GetRawText() : null;

        private static EtimsDeviceDto ToDto(EtimsDevice d, IReadOnlyDictionary<EtimsSubmissionStatus, int> counts) =>
            new(d.EtimsDeviceId, d.BranchId, d.Branch?.Name ?? "", d.Tin, d.BhfId, d.DeviceSerialNumber,
                d.CmcKey.Length <= 6 ? "******" : $"{d.CmcKey[..3]}...{d.CmcKey[^3..]}", d.SdcId, d.MrcNo, d.TaxpayerName, d.KraBranchName,
                d.DefaultItemClassCode, d.ReceiptTopMessage, d.ReceiptBottomMessage, d.IsActive, d.ActivatedAt, d.SweptUntil, d.LastInvoiceNumber,
                d.LastStockAdjustmentNumber, d.LastItemSequence, d.OpeningStockQueuedAt, d.LastCodeSyncAt,
                Enum.GetValues<EtimsSubmissionStatus>().ToDictionary(s => s.ToString(), s => counts.GetValueOrDefault(s)));

        internal static EtimsSubmissionDto ToDto(EtimsSubmission s) =>
            new(s.EtimsSubmissionId, s.EtimsDeviceId, s.BranchId, s.DocumentType, s.SourceType, s.SourceId, s.DocumentNumber, s.Endpoint,
                s.Status, s.Attempts, s.NextAttemptAt, s.LastAttemptAt, s.SentAt, s.ResultCode, s.ResultMessage, s.ReceiptNumber, s.CreatedAt);
    }
}
