using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Etims
{
    /// <summary>
    /// eTIMS for the platform, the third-party integrator every business reports to KRA through: register and look after any business's
    /// devices on its behalf, and watch what KRA refused across every business. An action on one business runs inside it, through
    /// <see cref="EtimsService"/> (so the same rules apply as when the business does it), and changes are recorded in its audit log.
    /// </summary>
    public sealed class PlatformEtimsService
    {
        private readonly IEtimsRepository _repository;
        private readonly EtimsService _etims;
        private readonly IPlatformRepository _platform;
        private readonly ITenantContext _tenant;
        private readonly ISystemEventRecorder _events;

        public PlatformEtimsService(IEtimsRepository repository, EtimsService etims, IPlatformRepository platform, ITenantContext tenant,
            ISystemEventRecorder events)
        {
            _repository = repository;
            _etims = etims;
            _platform = platform;
            _tenant = tenant;
            _events = events;
        }

        // ---------------------------------------------------------------- Across businesses

        public async Task<PlatformEtimsOverviewDto> GetOverviewAsync()
        {
            var businesses = (await _repository.GetBusinessesAnyBusinessAsync())
                .Select(b => new PlatformEtimsBusinessRowDto(b.BusinessId, b.BusinessName, b.KraPin, b.EtimsEnabled, b.IsActive, b.IsSuspended,
                    b.DeviceCount, b.ActiveDeviceCount, b.Counts.GetValueOrDefault(EtimsSubmissionStatus.Pending),
                    b.Counts.GetValueOrDefault(EtimsSubmissionStatus.Failed), b.Counts.GetValueOrDefault(EtimsSubmissionStatus.Rejected),
                    b.Counts.GetValueOrDefault(EtimsSubmissionStatus.Sent), b.LastSentAt, b.OldestProblemAt))
                .ToList();
            return new PlatformEtimsOverviewDto(businesses.Count, businesses.Sum(b => b.DeviceCount), businesses.Sum(b => b.ActiveDeviceCount),
                businesses.Sum(b => b.Pending), businesses.Sum(b => b.Failed), businesses.Sum(b => b.Rejected), businesses);
        }

        /// <summary>Submissions across every business; with no status given, what needs attention (Failed and Rejected).</summary>
        public async Task<PagedResult<PlatformEtimsSubmissionDto>> GetSubmissionsAsync(int page, int pageSize, PlatformEtimsSubmissionQuery query)
        {
            var paged = await _repository.GetSubmissionsAnyBusinessAsync(Math.Max(1, page), Math.Clamp(pageSize, 1, 200), query);
            return new PagedResult<PlatformEtimsSubmissionDto>(paged.Items.Select(r => new PlatformEtimsSubmissionDto(r.Submission.BusinessId,
                r.BusinessName, r.BranchName, r.Tin, r.BhfId, EtimsService.ToDto(r.Submission))).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
        }

        public async Task<EtimsSubmissionDetailDto> GetSubmissionAsync(Guid submissionId)
        {
            using (_tenant.Use(await SubmissionBusinessAsync(submissionId)))
                return await _etims.GetSubmissionAsync(submissionId);
        }

        public async Task<EtimsSubmissionDto> RetryAsync(Guid submissionId)
        {
            using (_tenant.Use(await SubmissionBusinessAsync(submissionId)))
            {
                var submission = await _etims.RetryAsync(submissionId);
                await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.SupportEtimsSubmissionRetried,
                    $"Platform support sent the {submission.DocumentType} KRA refused ({submission.ResultMessage ?? "no reason given"}) again.",
                    EntityType: "EtimsSubmission", EntityId: submission.EtimsSubmissionId, BranchId: submission.BranchId,
                    Data: new { submission.DocumentType, submission.SourceType, submission.SourceId, submission.ResultCode }));
                return submission;
            }
        }

        // ---------------------------------------------------------------- One business

        public async Task<PlatformEtimsBusinessDto> GetBusinessAsync(Guid businessId)
        {
            var business = await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
            {
                var devices = (await _etims.GetDevicesAsync()).ToDictionary(d => d.BranchId);
                var branches = await _repository.GetBranchesAsync();
                return new PlatformEtimsBusinessDto(business.BusinessId, business.BusinessName, business.KraPin, business.EtimsEnabled,
                    business.EtimsEnabledAt, branches.Select(b => new PlatformEtimsBranchDto(b.BranchId, b.Name, b.KraBranchId, b.IsActive,
                        devices.GetValueOrDefault(b.BranchId))).ToList());
            }
        }

        /// <summary>Registers a branch's device with KRA for the business, as <see cref="EtimsService.RegisterDeviceAsync"/>.</summary>
        public async Task<EtimsDeviceDto> RegisterDeviceAsync(Guid businessId, RegisterEtimsDeviceRequestDto request)
        {
            await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
            {
                var device = await _etims.RegisterDeviceAsync(request);
                await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.SupportEtimsDeviceRegistered,
                    $"Platform support registered KRA eTIMS device {device.DeviceSerialNumber} for {device.BranchName} (KRA branch {device.BhfId}).",
                    EntityType: "EtimsDevice", EntityId: device.EtimsDeviceId, BranchId: device.BranchId,
                    Data: new { device.Tin, device.BhfId, device.DeviceSerialNumber, device.SdcId, device.MrcNo }));
                return device;
            }
        }

        public async Task<EtimsDeviceDto> UpdateDeviceAsync(Guid businessId, Guid deviceId, UpdateEtimsDeviceRequestDto request)
        {
            await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
            {
                var device = await _etims.UpdateDeviceAsync(deviceId, request);
                await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.SupportEtimsDeviceUpdated,
                    $"Platform support updated KRA eTIMS device {device.DeviceSerialNumber} for {device.BranchName}" +
                    (device.IsActive ? "." : ", which no longer reports."),
                    EntityType: "EtimsDevice", EntityId: device.EtimsDeviceId, BranchId: device.BranchId,
                    Data: new { device.IsActive, device.DefaultItemClassCode }));
                return device;
            }
        }

        public async Task<IEnumerable<EtimsReferenceSyncDto>> SyncReferenceDataAsync(Guid businessId, Guid deviceId, CancellationToken cancellationToken = default)
        {
            await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
                return await _etims.SyncReferenceDataAsync(deviceId, cancellationToken);
        }

        public async Task<IEnumerable<EtimsItemClassDto>> SearchItemClassesAsync(Guid businessId, Guid deviceId, string? search, int take = 50)
        {
            await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
                return await _etims.SearchItemClassesAsync(deviceId, search, take);
        }

        public async Task<EtimsRunResultDto> RunNowAsync(Guid businessId, Guid deviceId, CancellationToken cancellationToken = default)
        {
            await RequireBusinessAsync(businessId);
            using (_tenant.Use(businessId))
                return await _etims.RunNowAsync(deviceId, cancellationToken);
        }

        // ----------------------------------------------------------------

        private async Task<BusinessProfile> RequireBusinessAsync(Guid businessId) =>
            await _platform.GetBusinessAsync(businessId) ?? throw new NotFoundException($"Business '{businessId}' not found.");

        private async Task<Guid> SubmissionBusinessAsync(Guid submissionId) =>
            await _repository.GetSubmissionBusinessIdAsync(submissionId)
            ?? throw new NotFoundException($"eTIMS submission '{submissionId}' not found.");
    }
}
