using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Helpers;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Licensing;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Platform
{
    /// <summary>
    /// What a super user does: onboard businesses, watch how they perform, issue and renew licences, suspend or close businesses,
    /// support a business's users, and manage other super users. A super user belongs to no business, so everything here works
    /// across businesses and is recorded in the affected business's audit log.
    /// </summary>
    public class PlatformService
    {
        private readonly IPlatformRepository _repository;
        private readonly IBusinessRepository _businesses;
        private readonly IAuthRepository _auth;
        private readonly IPasswordHasher _hasher;
        private readonly ISystemEventRecorder _events;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;
        private readonly IBusinessAccessGate _gate;
        private readonly LicensingSettings _licensing;
        private readonly NotificationService _notifications;

        public PlatformService(IPlatformRepository repository, IBusinessRepository businesses, IAuthRepository auth, IPasswordHasher hasher,
            ISystemEventRecorder events, ITenantContext tenant, ICurrentUser currentUser, IBusinessAccessGate gate, LicensingSettings licensing,
            NotificationService notifications)
        {
            _repository = repository;
            _businesses = businesses;
            _auth = auth;
            _hasher = hasher;
            _events = events;
            _tenant = tenant;
            _currentUser = currentUser;
            _gate = gate;
            _licensing = licensing;
            _notifications = notifications;
        }

        private static DateOnly Today => LicensePolicy.Today;

        // ================================================================ Onboarding

        /// <summary>
        /// Registers a business and hands it to its owner, who becomes its first member with the Admin role. The super user who
        /// registers it does NOT join it. The business starts on a trial licence when Licensing:TrialDays is set.
        /// </summary>
        public async Task<RegisterBusinessResultDto> RegisterBusinessAsync(RegisterBusinessRequestDto request)
        {
            var sector = request.Sector ?? BusinessSector.General;
            if (!Enum.IsDefined(sector))
                throw new ValidationException("sector", "Unknown sector.");
            var profile = BusinessDefaults.For(sector);
            if ((request.Kind ?? profile.Kind) is not { } kind || !Enum.IsDefined(kind))
                throw new ValidationException("kind", sector == BusinessSector.General
                    ? "Business kind is required (Manufacturer or Trader)."
                    : $"Business kind is required for the {sector} sector (Manufacturer or Trader): it can be either.");
            if (request.Owner is null)
                throw new ValidationException("owner", "The business's owner is required: they become its first administrator.");

            BusinessService.ValidateProfile(request.BusinessName, request.KraPin, request.MaxDiscountPercent);
            var mpesa = BusinessService.ResolveMpesaNumbers(request.MpesaTillNumber, request.MpesaPaybillNumber, request.MpesaShortCode);
            if (await _businesses.KraPinExistsAsync(request.KraPin.Trim()))
                throw new DuplicateBusinessException($"A business with KRA PIN '{request.KraPin}' is already registered.");
            await BusinessService.EnsureMpesaNumbersFreeAsync(_businesses, mpesa, null);

            var (owner, ownerIsNew, temporaryPassword) = await PrepareBusinessUserAsync(request.Owner, "owner");

            var business = new BusinessProfile
            {
                BusinessId = Guid.NewGuid(),
                BusinessName = request.BusinessName.Trim(),
                TradingName = request.TradingName,
                BusinessType = request.BusinessType,
                Kind = kind,
                Sector = sector,
                RegistrationNumber = request.RegistrationNumber,
                KraPin = request.KraPin.Trim(),
                PhysicalAddress = request.PhysicalAddress,
                County = request.County,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                LogoUrl = request.LogoUrl,
                BackgroundImageUrl = request.BackgroundImageUrl,
                IsVatRegistered = request.IsVatRegistered,
                MpesaShortCode = mpesa.ShortCode,
                MpesaTillNumber = mpesa.Till,
                MpesaPaybillNumber = mpesa.Paybill,
                MaxDiscountPercent = request.MaxDiscountPercent,
                AllowNegativeStock = request.AllowNegativeStock ?? profile.AllowNegativeStock,
                CreatedBy = _currentUser.UserId
            };

            BusinessLicense? trial = _licensing.TrialDays <= 0 ? null : new BusinessLicense
            {
                BusinessLicenseId = Guid.NewGuid(),
                BusinessId = business.BusinessId,
                Kind = LicenseKind.Trial,
                PlanName = "Trial",
                StartsOn = Today,
                EndsOn = Today.AddDays(_licensing.TrialDays - 1),
                CreatedBy = _currentUser.UserId,
            };

            using (_tenant.Use(business.BusinessId))
            {
                _events.Record(new SystemEventDraft(SystemEventTypes.BusinessRegistered,
                    $"{business.BusinessName} ({business.Sector}, {business.Kind}, {business.BusinessType}) was registered, owned by {owner.Username} ({owner.Email})" +
                    (trial is null ? "." : $", on a trial until {trial.EndsOn:yyyy-MM-dd}."),
                    EntityType: "Business", EntityId: business.BusinessId,
                    Data: new { business.KraPin, business.Sector, business.Kind, business.BusinessType, ownerEmail = owner.Email, trialEndsOn = trial?.EndsOn }));
                _events.Record(new SystemEventDraft(SystemEventTypes.BusinessOwnerAdded,
                    $"You are now the administrator of {business.BusinessName} on Ultimate POS. Sign in with {owner.Email}.",
                    EntityType: "User", EntityId: owner.UserId, SubjectUserId: owner.UserId));

                var created = await _repository.CreateBusinessAsync(business, owner, ownerIsNew, trial, Today);
                return new RegisterBusinessResultDto(ToBusinessDto(created),
                    new BusinessOwnerResultDto(owner.UserId, owner.Email, owner.Username, ownerIsNew, temporaryPassword),
                    trial is null ? null : ToDto(trial));
            }
        }

        /// <summary>Adds another administrator to a business (the owner left, a second manager...).</summary>
        public async Task<BusinessOwnerResultDto> AddBusinessAdminAsync(Guid businessId, BusinessOwnerRequestDto request)
        {
            var business = await RequireBusinessAsync(businessId);
            var (user, isNew, temporaryPassword) = await PrepareBusinessUserAsync(request, "");

            using (_tenant.Use(businessId))
            {
                _events.Record(new SystemEventDraft(SystemEventTypes.BusinessOwnerAdded,
                    $"You are now an administrator of {business.BusinessName} on Ultimate POS. Sign in with {user.Email}.",
                    EntityType: "User", EntityId: user.UserId, SubjectUserId: user.UserId));
                await _repository.AddBusinessAdminAsync(businessId, user, isNew);
            }
            return new BusinessOwnerResultDto(user.UserId, user.Email, user.Username, isNew, temporaryPassword);
        }

        /// <summary>
        /// The account that will run a business: an existing user found by email (never a super user), or a new one with the given
        /// or a generated password, flagged to change it at first sign-in.
        /// </summary>
        private async Task<(User User, bool IsNew, string? TemporaryPassword)> PrepareBusinessUserAsync(BusinessOwnerRequestDto request, string prefix)
        {
            string Field(string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors[Field("fullName")] = new[] { "The full name is required." };
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                errors[Field("email")] = new[] { "A valid email address is required." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var email = request.Email.Trim();
            var existing = await _auth.GetUserAsync(email);
            if (existing is not null)
            {
                if (await _repository.IsSuperUserAsync(existing.UserId))
                    throw new ValidationException(Field("email"), "That is a super user account. Super users cannot belong to a business — use another email.");
                if (!existing.IsActive)
                    throw new ValidationException(Field("email"), "That account is deactivated.");
                return (existing, false, null);
            }

            string? temporary = null;
            var password = request.Password;
            if (string.IsNullOrEmpty(password))
                password = temporary = PasswordGenerator.Generate();
            else if (AuthService.PasswordError(password) is { } passwordError)
                throw new ValidationException(Field("password"), passwordError);

            var (hash, salt) = _hasher.HashPassword(password);
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Email = email,
                Username = request.FullName.Trim(),
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                PasswordHash = hash,
                PasswordSalt = salt,
                MustChangePassword = true,
                CreatedBy = _currentUser.UserId
            };
            return (user, true, temporary);
        }

        // ================================================================ Businesses and performance

        /// <param name="sector">Only businesses of this sector. Null = all.</param>
        public async Task<IEnumerable<BusinessSummaryDto>> GetBusinessesAsync(BusinessSector? sector = null)
        {
            var businesses = (await _repository.GetBusinessesAsync()).Where(b => sector is null || b.Sector == sector);
            var activity = await _repository.GetActivityAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            return businesses.Select(b => Summary(b, activity.GetValueOrDefault(b.BusinessId)?.ActiveUsers ?? 0,
                activity.GetValueOrDefault(b.BusinessId)?.ActiveBranches ?? 0));
        }

        public async Task<BusinessDetailDto> GetBusinessAsync(Guid businessId)
        {
            var business = await RequireBusinessAsync(businessId);
            var to = DateTime.UtcNow;
            var activity = (await _repository.GetActivityAsync(to.AddDays(-30), to, businessId)).GetValueOrDefault(businessId);
            var licenses = await _repository.GetLicensesAsync(businessId);
            var members = await _auth.GetBusinessUsersAsync(businessId);

            return new BusinessDetailDto(Summary(business, activity?.ActiveUsers ?? 0, activity?.ActiveBranches ?? 0), licenses.Select(ToDto),
                members.Select(m => new UserDto(m.User.UserId, m.User.Email, m.User.Username, m.User.PhoneNumber, m.User.IsActive && m.MembershipActive,
                    m.User.IsLocked, m.User.UserRoles.Select(r => r.Role.RoleName), Array.Empty<string>())),
                Performance(business, activity));
        }

        /// <summary>How every business is doing over a period: sales, users, activity and licence position, plus platform totals.</summary>
        public async Task<PlatformDashboardDto> GetDashboardAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes)
        {
            var toDay = to ?? ReportPeriod.TodayAt(utcOffsetMinutes);
            var fromDay = from ?? toDay.AddDays(-29);
            var period = ReportPeriod.Create(fromDay, toDay, utcOffsetMinutes);

            var businesses = await _repository.GetBusinessesAsync();
            var activity = await _repository.GetActivityAsync(period.FromUtc, period.ToUtcExclusive);
            var rows = businesses
                .Select(b => Performance(b, activity.GetValueOrDefault(b.BusinessId)))
                .OrderByDescending(r => r.Revenue).ThenBy(r => r.BusinessName)
                .ToList();
            int Count(string status) => rows.Count(r => r.AccessStatus == status);

            var bySector = rows.GroupBy(r => r.Sector)
                .Select(g => new SectorPerformanceDto(g.Key, g.Count(), g.Sum(r => r.SaleCount), g.Sum(r => r.Revenue)))
                .OrderByDescending(s => s.Revenue).ThenBy(s => s.Sector)
                .ToList();

            return new PlatformDashboardDto(period.From, period.To, rows.Count, Count("Active"), Count("ExpiringSoon"), Count("GracePeriod"),
                Count("Expired"), Count("Unlicensed"), Count("Suspended"), Count("Closed"),
                rows.Sum(r => r.SaleCount), rows.Sum(r => r.Revenue), await _repository.GetLicenseIncomeAsync(period.FromUtc, period.ToUtcExclusive),
                rows, bySector);
        }

        // ================================================================ Suspend / close

        /// <summary>Locks the business (e.g. unpaid licence). Its users are refused at once; nothing is deleted.</summary>
        public Task<BusinessSummaryDto> SuspendAsync(Guid businessId, ReasonRequestDto request)
        {
            var reason = RequireReason(request?.Reason);
            return ChangeStatusAsync(businessId, b =>
            {
                if (b.IsSuspended) throw new InvalidAssignmentException($"{b.BusinessName} is already suspended.");
                b.IsSuspended = true;
                b.SuspendedAt = DateTime.UtcNow;
                b.SuspendedBy = _currentUser.UserId;
                b.SuspensionReason = reason;
            }, b => new SystemEventDraft(SystemEventTypes.BusinessSuspended,
                $"{b.BusinessName} was suspended: {reason}. Its users cannot sign in until it is reactivated.",
                EventSeverity.Critical, "Business", b.BusinessId, new { reason }));
        }

        public Task<BusinessSummaryDto> ReactivateAsync(Guid businessId) =>
            ChangeStatusAsync(businessId, b =>
            {
                if (!b.IsSuspended) throw new InvalidAssignmentException($"{b.BusinessName} is not suspended.");
                b.IsSuspended = false;
                b.SuspendedAt = null;
                b.SuspendedBy = null;
                b.SuspensionReason = null;
            }, b => new SystemEventDraft(SystemEventTypes.BusinessReactivated, $"{b.BusinessName} was reactivated. Its users can sign in again.",
                EntityType: "Business", EntityId: b.BusinessId));

        /// <summary>Closes the business for good (it stopped trading, left the platform). Reversible with Reopen; data is kept.</summary>
        public Task<BusinessSummaryDto> CloseAsync(Guid businessId, string? reason) =>
            ChangeStatusAsync(businessId, b =>
            {
                if (!b.IsActive) throw new InvalidAssignmentException($"{b.BusinessName} is already closed.");
                b.IsActive = false;
            }, b => new SystemEventDraft(SystemEventTypes.BusinessClosed,
                $"{b.BusinessName} was closed{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}")}.",
                EventSeverity.Critical, "Business", b.BusinessId, new { reason }));

        public Task<BusinessSummaryDto> ReopenAsync(Guid businessId) =>
            ChangeStatusAsync(businessId, b =>
            {
                if (b.IsActive) throw new InvalidAssignmentException($"{b.BusinessName} is not closed.");
                b.IsActive = true;
            }, b => new SystemEventDraft(SystemEventTypes.BusinessReopened, $"{b.BusinessName} was reopened.",
                EntityType: "Business", EntityId: b.BusinessId));

        /// <summary>
        /// Reclassifies a business (registered as General before sectors existed, or simply wrong). Only the label moves: what it started
        /// with — units, expense categories, AllowNegativeStock — stays as it is, since the business may have changed it since.
        /// </summary>
        public Task<BusinessSummaryDto> ChangeSectorAsync(Guid businessId, ChangeSectorRequestDto request)
        {
            if (request is null || !Enum.IsDefined(request.Sector))
                throw new ValidationException("sector", "Unknown sector.");
            BusinessSector? from = null;
            return ChangeStatusAsync(businessId, b =>
            {
                if (b.Sector == request.Sector) throw new InvalidAssignmentException($"{b.BusinessName} is already in the {b.Sector} sector.");
                from ??= b.Sector; // the change runs twice (validate, then save): keep the sector it had to begin with
                b.Sector = request.Sector;
            }, b => new SystemEventDraft(SystemEventTypes.BusinessSectorChanged,
                $"{b.BusinessName} was moved from the {from} sector to {request.Sector}.",
                EntityType: "Business", EntityId: b.BusinessId, Data: new { from, to = request.Sector }));
        }

        private async Task<BusinessSummaryDto> ChangeStatusAsync(Guid businessId, Action<BusinessProfile> change, Func<BusinessProfile, SystemEventDraft> describe)
        {
            var business = await RequireBusinessAsync(businessId);
            change(business); // validates against the current state before anything is written
            using (_tenant.Use(businessId))
            {
                _events.Record(describe(business));
                var updated = await _repository.UpdateBusinessStatusAsync(businessId, change);
                _gate.Invalidate(businessId);
                return Summary(updated, await _repository.CountActiveMembersAsync(businessId));
            }
        }

        // ================================================================ Licences

        public async Task<IEnumerable<BusinessLicenseDto>> GetLicensesAsync(Guid businessId)
        {
            await RequireBusinessAsync(businessId);
            return (await _repository.GetLicensesAsync(businessId)).Select(ToDto);
        }

        /// <summary>Licences ending within the next <paramref name="withinDays"/> days (and those already ended but in grace), soonest first.</summary>
        public async Task<IEnumerable<BusinessLicenseDto>> GetExpiringLicensesAsync(int withinDays)
        {
            if (withinDays is < 0 or > 365)
                throw new ValidationException("withinDays", "withinDays must be between 0 and 365.");
            return (await _repository.GetLicensesEndingBetweenAsync(Today.AddDays(-_licensing.GraceDays), Today.AddDays(withinDays))).Select(ToDto);
        }

        /// <summary>Issues a licence period — a renewal when the business already has one. Lifts nothing else: a suspension stays until reactivated.</summary>
        public async Task<BusinessLicenseDto> IssueLicenseAsync(Guid businessId, IssueLicenseRequestDto request)
        {
            var business = await RequireBusinessAsync(businessId);

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.PlanName))
                errors["planName"] = new[] { "A plan name is required." };
            if (!Enum.IsDefined(request.Kind))
                errors["kind"] = new[] { "Kind must be Trial, Paid or Complimentary." };
            if (request.Amount < 0 || decimal.Round(request.Amount, 2) != request.Amount)
                errors["amount"] = new[] { "Amount must be zero or more, with at most 2 decimal places." };
            if (request.Kind == LicenseKind.Paid && request.Amount <= 0)
                errors["amount"] = new[] { "A paid licence needs the amount paid." };
            if (request.Kind == LicenseKind.Paid && request.PaymentMethod is { } method && method != Domain.Enums.PosEnums.SettlementMethod.Cash
                && string.IsNullOrWhiteSpace(request.PaymentReference))
                errors["paymentReference"] = new[] { "Give the payment reference (M-Pesa code, bank reference or cheque number)." };
            if (request.MaxUsers is < 1 or > 10000)
                errors["maxUsers"] = new[] { "Max users must be between 1 and 10000, or left out for unlimited." };
            if (request.MaxBranches is < 1 or > Branch.MaxBranchNumber + 1)
                errors["maxBranches"] = new[] { $"Max branches must be between 1 and {Branch.MaxBranchNumber + 1}, or left out for unlimited." };
            if (request.Months is not null && request.EndsOn is not null)
                errors["months"] = new[] { "Give either months or an end date, not both." };
            else if (request.Months is null && request.EndsOn is null)
                errors["months"] = new[] { "Give the number of months (1–36) or an end date." };
            else if (request.Months is < 1 or > 36)
                errors["months"] = new[] { "Months must be between 1 and 36." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            // A renewal continues from the day after the current period; a lapsed or first licence starts today.
            var startsOn = request.StartsOn
                ?? (business.LicenseEndsOn is { } current && current >= Today ? current.AddDays(1) : Today);
            var endsOn = request.EndsOn ?? startsOn.AddMonths(request.Months!.Value).AddDays(-1);
            if (endsOn < startsOn)
                throw new ValidationException("endsOn", "The end date must be on or after the start date.");

            var overlapping = (await _repository.GetLicensesAsync(businessId))
                .FirstOrDefault(l => l.State == LicenseState.Active && l.StartsOn <= endsOn && l.EndsOn >= startsOn);
            if (overlapping is not null)
                throw new ValidationException("startsOn",
                    $"This period overlaps {overlapping.LicenseNumber} ({overlapping.StartsOn:yyyy-MM-dd} to {overlapping.EndsOn:yyyy-MM-dd}). Start after it, or cancel it first.");

            var license = new BusinessLicense
            {
                BusinessLicenseId = Guid.NewGuid(),
                BusinessId = businessId,
                Kind = request.Kind,
                PlanName = request.PlanName.Trim(),
                MaxUsers = request.MaxUsers,
                MaxBranches = request.MaxBranches,
                StartsOn = startsOn,
                EndsOn = endsOn,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim(),
                Notes = request.Notes,
                CreatedBy = _currentUser.UserId,
            };

            using (_tenant.Use(businessId))
            {
                _events.Record(new SystemEventDraft(SystemEventTypes.LicenseIssued,
                    $"{business.BusinessName}: {license.Kind} licence ({license.PlanName}) from {startsOn:yyyy-MM-dd} to {endsOn:yyyy-MM-dd}" +
                    (license.Amount > 0 ? $", {license.Amount:0.00} {license.Currency} paid." : "."),
                    EntityType: "Business", EntityId: businessId,
                    Data: new { license.Kind, license.PlanName, startsOn, endsOn, license.Amount, license.MaxUsers, license.MaxBranches, license.PaymentReference },
                    ClearsAlertKeys: business.LicenseEndsOn is { } previous
                        ? new[] { AlertKeys.LicenseExpiring(previous), AlertKeys.LicenseExpired(previous) } : null));
                var saved = await _repository.AddLicenseAsync(license, Today);
                _gate.Invalidate(businessId);
                return ToDto(saved);
            }
        }

        public async Task<BusinessLicenseDto> CancelLicenseAsync(Guid licenseId, ReasonRequestDto request)
        {
            var reason = RequireReason(request?.Reason);
            var license = await _repository.GetLicenseAsync(licenseId)
                ?? throw new NotFoundException($"Licence '{licenseId}' not found.");
            if (license.State == LicenseState.Cancelled)
                throw new InvalidAssignmentException($"{license.LicenseNumber} is already cancelled.");
            var business = await RequireBusinessAsync(license.BusinessId);

            using (_tenant.Use(license.BusinessId))
            {
                _events.Record(new SystemEventDraft(SystemEventTypes.LicenseCancelled,
                    $"{business.BusinessName}: licence {license.LicenseNumber} ({license.StartsOn:yyyy-MM-dd} to {license.EndsOn:yyyy-MM-dd}) was cancelled: {reason}",
                    EventSeverity.Warning, "Business", license.BusinessId, new { license.LicenseNumber, reason }));
                var cancelled = await _repository.CancelLicenseAsync(licenseId, reason, _currentUser.UserId, Today);
                _gate.Invalidate(license.BusinessId);
                return ToDto(cancelled);
            }
        }

        // ================================================================ Support

        public async Task<IEnumerable<UserDto>> GetBusinessUsersAsync(Guid businessId) => (await GetBusinessAsync(businessId)).Members;

        /// <summary>Resets a business user's password (they're locked out, the owner forgot it). Returns the new temporary password once.</summary>
        public async Task<CreatedAccountDto> ResetBusinessUserPasswordAsync(Guid businessId, Guid userId)
        {
            var (member, business) = await RequireMemberAsync(businessId, userId);
            var temporary = PasswordGenerator.Generate();
            var (hash, salt) = _hasher.HashPassword(temporary);

            var user = (await _auth.GetUserForUpdateAsync(userId))!;
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            user.MustChangePassword = true;
            user.IsLocked = false;
            user.FailedLoginAttempts = 0;
            user.LastUpdatedBy = _currentUser.UserId;
            user.LastUpdatedAt = DateTime.UtcNow;

            using (_tenant.Use(businessId))
            {
                _events.Record(new SystemEventDraft(SystemEventTypes.SupportPasswordReset,
                    $"Ultimate POS support reset the password for {user.Email}. You'll be asked to choose a new one when you sign in.",
                    EventSeverity.Warning, "User", userId, SubjectUserId: userId));
                await _auth.UpdateUserAsync(user);
            }
            await _auth.RevokeAllSessionsAsync(userId);
            return new CreatedAccountDto(ToUserDto(member, user), temporary);
        }

        public async Task<UserDto> UnlockBusinessUserAsync(Guid businessId, Guid userId)
        {
            var (member, business) = await RequireMemberAsync(businessId, userId);
            using (_tenant.Use(businessId))
            {
                await _auth.SetUserLockStatusAsync(userId, false, _currentUser.UserId);
                await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.SupportUserUnlocked,
                    $"Ultimate POS support unlocked {member.User.Email}.", EntityType: "User", EntityId: userId, SubjectUserId: userId));
            }
            return ToUserDto(member, (await _auth.GetUserForUpdateAsync(userId))!);
        }

        /// <summary>A business's audit log (support), or the platform's own events when no business is given.</summary>
        public async Task<PagedResult<AuditLogEntryDto>> GetAuditLogAsync(Guid? businessId, int page, int pageSize, DateOnly? from, DateOnly? to,
            int utcOffsetMinutes, string? eventType, Guid? actorUserId, string? search)
        {
            if (businessId is not { } b)
                return await _notifications.GetPlatformAuditLogAsync(page, pageSize, from, to, utcOffsetMinutes, eventType, actorUserId, search);

            await RequireBusinessAsync(b);
            using (_tenant.Use(b))
                return await _notifications.GetAuditLogAsync(page, pageSize, from, to, utcOffsetMinutes, eventType, actorUserId, null, null, null, search);
        }

        // ================================================================ Super users

        public async Task<IEnumerable<UserDto>> GetSuperUsersAsync() =>
            (await _repository.GetSuperUsersAsync()).Select(u => new UserDto(u.UserId, u.Email, u.Username, u.PhoneNumber, u.IsActive, u.IsLocked,
                new[] { PermissionCatalog.SuperUserRole }, Array.Empty<string>()));

        public async Task<CreatedAccountDto> CreateSuperUserAsync(CreateSuperUserRequestDto request)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors["fullName"] = new[] { "The full name is required." };
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                errors["email"] = new[] { "A valid email address is required." };
            if (!string.IsNullOrEmpty(request.Password) && AuthService.PasswordError(request.Password) is { } passwordError)
                errors["password"] = new[] { passwordError };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            // A super user is a separate account: never one that works in a business.
            if (await _auth.GetUserAsync(request.Email.Trim()) is not null)
                throw new ValidationException("email", "An account with this email already exists. Super users need their own account, separate from any business.");

            var temporary = string.IsNullOrEmpty(request.Password) ? PasswordGenerator.Generate() : null;
            var (hash, salt) = _hasher.HashPassword(temporary ?? request.Password!);
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Email = request.Email.Trim(),
                Username = request.FullName.Trim(),
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                PasswordHash = hash,
                PasswordSalt = salt,
                MustChangePassword = true,
                CreatedBy = _currentUser.UserId
            };
            _events.Record(new SystemEventDraft(SystemEventTypes.SuperUserCreated,
                $"{user.Username} ({user.Email}) was made a super user of Ultimate POS.", EventSeverity.Warning, "User", user.UserId,
                BusinessId: null, SubjectUserId: user.UserId));
            var created = await _repository.CreateSuperUserAsync(user);
            return new CreatedAccountDto(new UserDto(created.UserId, created.Email, created.Username, created.PhoneNumber, true, false,
                new[] { PermissionCatalog.SuperUserRole }, Array.Empty<string>()), temporary);
        }

        public async Task<UserDto> SetSuperUserActiveAsync(Guid userId, bool isActive)
        {
            if (!await _repository.IsSuperUserAsync(userId))
                throw new NotFoundException($"Super user '{userId}' not found.");
            if (!isActive && userId == _currentUser.UserId)
                throw new InvalidAssignmentException("You cannot deactivate your own super user account.");
            if (!isActive && await _repository.CountActiveSuperUsersAsync() <= 1)
                throw new InvalidAssignmentException("This is the last active super user. Create another before deactivating it.");

            var user = (await _auth.GetUserForUpdateAsync(userId))!;
            user.IsActive = isActive;
            user.LastUpdatedBy = _currentUser.UserId;
            user.LastUpdatedAt = DateTime.UtcNow;
            _events.Record(new SystemEventDraft(isActive ? SystemEventTypes.SuperUserActivated : SystemEventTypes.SuperUserDeactivated,
                $"Super user {user.Username} ({user.Email}) was {(isActive ? "reactivated" : "deactivated")}.", EventSeverity.Warning, "User", userId,
                BusinessId: null, SubjectUserId: userId));
            await _auth.UpdateUserAsync(user);
            if (!isActive)
                await _auth.RevokeAllSessionsAsync(userId);
            return new UserDto(user.UserId, user.Email, user.Username, user.PhoneNumber, user.IsActive, user.IsLocked,
                new[] { PermissionCatalog.SuperUserRole }, Array.Empty<string>());
        }

        // ================================================================ Helpers

        private async Task<BusinessProfile> RequireBusinessAsync(Guid businessId) =>
            await _repository.GetBusinessAsync(businessId) ?? throw new NotFoundException($"Business '{businessId}' not found.");

        private async Task<((User User, bool MembershipActive) Member, BusinessProfile Business)> RequireMemberAsync(Guid businessId, Guid userId)
        {
            var business = await RequireBusinessAsync(businessId);
            var member = await _auth.GetBusinessUserAsync(businessId, userId)
                ?? throw new NotFoundException($"User '{userId}' is not a member of {business.BusinessName}.");
            return (member, business);
        }

        private static string RequireReason(string? reason) =>
            string.IsNullOrWhiteSpace(reason) ? throw new ValidationException("reason", "A reason is required.")
            : reason.Trim().Length > 300 ? throw new ValidationException("reason", "The reason cannot exceed 300 characters.")
            : reason.Trim();

        private BusinessSummaryDto Summary(BusinessProfile b, int activeUsers, int activeBranches = 0) =>
            new(ToBusinessDto(b), ToAccessDto(LicensePolicy.Evaluate(b, _licensing, Today)), activeUsers, b.CreatedAt, activeBranches);

        private BusinessPerformanceDto Performance(BusinessProfile b, BusinessActivityRow? a) =>
            new(b.BusinessId, b.TradingName ?? b.BusinessName, b.Kind, b.BusinessType, LicensePolicy.Evaluate(b, _licensing, Today).Status, b.LicenseEndsOn,
                a?.ActiveUsers ?? 0, a?.ActiveProducts ?? 0, a?.SaleCount ?? 0, a?.Revenue ?? 0m,
                a is { SaleCount: > 0 } ? Math.Round(a.Revenue / a.SaleCount, 2, MidpointRounding.AwayFromZero) : 0m,
                a?.LastSaleAt, a?.LastSignInAt, b.CreatedAt, a?.ActiveBranches ?? 0, b.Sector);

        internal static BusinessAccessDto ToAccessDto(BusinessAccess a) => new(a.Status, a.Allowed, a.Code, a.Message, a.LicenseEndsOn, a.DaysRemaining);

        internal static BusinessDto ToBusinessDto(BusinessProfile b) =>
            new(b.BusinessId, b.BusinessName, b.TradingName, b.BusinessType, b.Kind, b.RegistrationNumber, b.KraPin,
                b.PhysicalAddress, b.County, b.PhoneNumber, b.Email, b.LogoUrl, b.BackgroundImageUrl, b.IsActive,
                b.IsVatRegistered, b.MpesaShortCode, b.MaxDiscountPercent, b.IsSuspended, b.SuspensionReason, b.LicenseEndsOn, b.LicenseMaxUsers,
                b.LicenseMaxBranches, b.AllowNegativeStock, b.Sector, BusinessModules.For(b.Sector, b.Kind), b.MpesaTillNumber, b.MpesaPaybillNumber,
                b.MonthlyDiscountThreshold, b.MonthlyDiscountPercent, b.MonthlyDiscountScope);

        internal static BusinessLicenseDto ToDto(BusinessLicense l) =>
            new(l.BusinessLicenseId, l.BusinessId, l.LicenseNumber, l.Kind, l.PlanName, l.MaxUsers, l.StartsOn, l.EndsOn, l.Amount, l.Currency,
                l.PaymentMethod, l.PaymentReference, l.Notes, l.State, l.CreatedAt, l.CreatedBy, l.CancelledAt, l.CancelReason, l.MaxBranches);

        private static UserDto ToUserDto((User User, bool MembershipActive) member, User current) =>
            new(current.UserId, current.Email, current.Username, current.PhoneNumber, current.IsActive && member.MembershipActive, current.IsLocked,
                member.User.UserRoles.Select(r => r.Role.RoleName), Array.Empty<string>());
    }
}
