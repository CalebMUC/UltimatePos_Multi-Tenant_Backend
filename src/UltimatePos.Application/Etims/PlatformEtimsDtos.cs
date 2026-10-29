using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Etims
{
    /// <summary>One business's eTIMS position on the platform's overview.</summary>
    /// <param name="OldestProblemAt">When the oldest Failed or Rejected submission was queued. Null = nothing needs attention.</param>
    public record PlatformEtimsBusinessRowDto(Guid BusinessId, string BusinessName, string KraPin, bool EtimsEnabled, bool BusinessActive,
        bool BusinessSuspended, int DeviceCount, int ActiveDeviceCount, int Pending, int Failed, int Rejected, int Sent, DateTime? LastSentAt,
        DateTime? OldestProblemAt);

    /// <summary>Every business that reports to KRA through the platform, and the totals across them.</summary>
    public record PlatformEtimsOverviewDto(int BusinessCount, int DeviceCount, int ActiveDeviceCount, int Pending, int Failed, int Rejected,
        IEnumerable<PlatformEtimsBusinessRowDto> Businesses);

    /// <param name="Device">Null = the branch has no eTIMS device yet.</param>
    public record PlatformEtimsBranchDto(Guid BranchId, string Name, string KraBranchId, bool IsActive, EtimsDeviceDto? Device);

    /// <summary>A business's eTIMS set-up: each branch and its device, for registering the ones still missing.</summary>
    public record PlatformEtimsBusinessDto(Guid BusinessId, string BusinessName, string KraPin, bool EtimsEnabled, DateTime? EtimsEnabledAt,
        IEnumerable<PlatformEtimsBranchDto> Branches);

    /// <summary>A submission with the business and device it went through.</summary>
    public record PlatformEtimsSubmissionDto(Guid BusinessId, string BusinessName, string BranchName, string Tin, string BhfId,
        EtimsSubmissionDto Submission);
}
